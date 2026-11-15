using ValidationSystem.Core.Services;

namespace ValidationSystem.IntegrationTests;

/// <summary>
/// Executes the 7 saved example validations end-to-end against SQL Server
/// (stored JSON -> catalog validation -> generated SQL -> read-only execution -> reasons -> snapshot)
/// and checks the exact violating records documented in db/05_seed_business_data.sql.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ExampleValidationsTests(DatabaseFixture fx)
{
    public static TheoryData<string, int[]> Examples => new()
    {
        { "עובדים קטינים עם שכר גבוה",              [13, 14] },
        { "תאריך סיום לפני תאריך התחלה",             [11, 12] },
        { "עובדים פעילים במחלקות לא פעילות",          [18, 19] },
        { "נתוני חובה חסרים",                        [19, 21, 22, 23, 24, 25] },
        { "שכר מעל תקרת התפקיד",                     [9, 13, 14, 16] },
        { "עובד פעיל תחת מחלקה או מנהל לא פעילים",    [18, 19, 26] },
        { "מחלקות שמנהלן אינו פעיל",                 [5] },          // Department root entity
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Example_validation_returns_exactly_the_expected_records(string name, int[] expectedKeys)
    {
        var (run, results) = await ExecuteAsync(name);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Null(run.ErrorMessage);
        Assert.Equal(expectedKeys.Length, run.ViolationCount);
        Assert.Equal(expectedKeys.Length, run.StoredResultCount);
        Assert.False(run.IsTruncated);
        Assert.NotNull(run.DurationMs);
        Assert.StartsWith("SELECT TOP (@maxRows)", run.GeneratedSql);
        Assert.Equal(expectedKeys.Select(k => k.ToString()), results.Items.Select(r => r.RecordKey));
        Assert.All(results.Items, r => Assert.NotEqual("[]", r.ReasonsJson));   // every record explains itself
    }

    [Fact]
    public async Task V1_reasons_show_computed_age_and_salary()
    {
        var reasons = await ReasonsAsync("עובדים קטינים עם שכר גבוה", "13");
        Assert.Equal(["גיל קטן מ-18 (ערך בפועל: 16)", "שכר חודשי גדול מ-15,000 (ערך בפועל: 16,500)"], reasons);
    }

    [Fact]
    public async Task V2_reason_compares_two_dates()
    {
        var reasons = await ReasonsAsync("תאריך סיום לפני תאריך התחלה", "11");
        Assert.Equal(["תאריך סיום העסקה (30/06/2013) מוקדם מ-\"תאריך תחילת העסקה\" (01/01/2014)"], reasons);
    }

    [Fact]
    public async Task V3_reasons_use_business_labels_through_relationship()
    {
        var reasons = await ReasonsAsync("עובדים פעילים במחלקות לא פעילות", "18");
        Assert.Equal(["סטטוס עובד: פעיל", "מחלקה › סטטוס מחלקה: לא פעילה"], reasons);
    }

    [Fact]
    public async Task V4_record_missing_two_fields_gets_two_reasons()
    {
        var reasons = await ReasonsAsync("נתוני חובה חסרים", "25");   // national id NULL + whitespace email
        Assert.Equal(["חסר ערך בשדה \"תעודת זהות\"", "חסר ערך בשדה \"דואר אלקטרוני\""], reasons);
    }

    [Fact]
    public async Task V5_reason_compares_salary_with_related_position_maximum()
    {
        var reasons = await ReasonsAsync("שכר מעל תקרת התפקיד", "9");
        Assert.Equal(["שכר חודשי (52,000) גדול מ-\"תפקיד › שכר מקסימלי לתפקיד\" (35,000)"], reasons);
    }

    [Fact]
    public async Task V6_nested_or_reports_each_true_branch()
    {
        // E1018: department AND manager inactive -> 3 reasons; E1026: only manager inactive -> 2 reasons.
        Assert.Equal(["סטטוס עובד: פעיל", "מחלקה › סטטוס מחלקה: לא פעילה", "מנהל ישיר › סטטוס עובד: לא פעיל"],
                     await ReasonsAsync("עובד פעיל תחת מחלקה או מנהל לא פעילים", "18"));
        Assert.Equal(["סטטוס עובד: פעיל", "מנהל ישיר › סטטוס עובד: לא פעיל"],
                     await ReasonsAsync("עובד פעיל תחת מחלקה או מנהל לא פעילים", "26"));
    }

    [Fact]
    public async Task Results_can_be_searched_filtered_by_condition_and_paged()
    {
        var (run, _) = await ExecuteAsync("נתוני חובה חסרים");

        var byText = await fx.Service.GetResultsAsync(run.RunId, new ResultQuery("E1025", null));
        Assert.Equal("25", Assert.Single(byText.Items).RecordKey);

        var byReasonText = await fx.Service.GetResultsAsync(run.RunId, new ResultQuery("תעודת זהות", null));
        Assert.Equal(["21", "25"], byReasonText.Items.Select(i => i.RecordKey));

        var byCondition = await fx.Service.GetResultsAsync(run.RunId, new ResultQuery(null, "c2"));   // email missing
        Assert.Equal(["19", "22", "25"], byCondition.Items.Select(i => i.RecordKey));

        var page2 = await fx.Service.GetResultsAsync(run.RunId, new ResultQuery(null, null, Page: 2, PageSize: 4));
        Assert.Equal(6, page2.TotalCount);
        Assert.Equal(["24", "25"], page2.Items.Select(i => i.RecordKey));

        var wildcard = await fx.Service.GetResultsAsync(run.RunId, new ResultQuery("%", null));
        Assert.Equal(0, wildcard.TotalCount);   // LIKE wildcards in search are literal
    }

    [Fact]
    public async Task Execution_history_is_recorded_with_latest_status_in_list()
    {
        var id = await fx.ValidationIdByNameAsync("שכר מעל תקרת התפקיד");
        var run = await fx.Service.ExecuteAsync(id);

        var history = await fx.Service.ListRunsAsync(id);
        Assert.Contains(history, r => r.RunId == run.RunId && r.Status == RunStatus.Succeeded);

        var summary = (await fx.Service.ListAsync("תקרת", null)).Single(v => v.Id == id);
        Assert.True(summary.LastRunId >= run.RunId);
        Assert.Equal(4, summary.LastRunViolationCount);
        Assert.Equal(RunStatus.Succeeded, summary.LastRunStatus);
    }

    private async Task<(RunInfo Run, PagedResults Results)> ExecuteAsync(string name)
    {
        var id = await fx.ValidationIdByNameAsync(name);
        var run = await fx.Service.ExecuteAsync(id);
        var results = await fx.Service.GetResultsAsync(run.RunId, new ResultQuery(null, null, 1, 500));
        return (run, results);
    }

    private async Task<string[]> ReasonsAsync(string validationName, string recordKey)
    {
        var (_, results) = await ExecuteAsync(validationName);
        var record = results.Items.Single(r => r.RecordKey == recordKey);
        var reasons = System.Text.Json.JsonSerializer.Deserialize<List<ReasonRow>>(record.ReasonsJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        return reasons.Select(r => r.Message).ToArray();
    }

    private sealed record ReasonRow(string ConditionId, string FieldPath, string Message);
}
