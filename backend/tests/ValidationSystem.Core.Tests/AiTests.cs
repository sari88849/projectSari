using Microsoft.Extensions.Logging.Abstractions;
using ValidationSystem.Core.Ai;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Services;
using ValidationSystem.Core.Validation;

namespace ValidationSystem.Core.Tests;

public class AiResponseTranslatorTests
{
    [Fact]
    public void Translates_nested_ai_response_into_engine_definition_with_generated_ids()
    {
        var t = AiResponseTranslator.Translate("""
        { "status": "ok", "name": "בדיקה", "rootEntity": "Employee", "logic": "And", "confidence": 0.9,
          "conditions": [
            { "field": "IsActive", "operator": "Equals", "value": true },
            { "logic": "Or", "conditions": [
                { "field": "EndDate", "operator": "LessThan", "compareToField": "StartDate" },
                { "field": "DepartmentId", "operator": "In", "values": [5, 6] },
                { "field": "Salary", "operator": "Between", "value": 1, "valueTo": 2 } ] } ] }
        """);

        Assert.Empty(t.Errors);
        var d = t.Definition!;
        Assert.Equal("Employee", d.RootEntity);
        Assert.Equal("root", d.RootGroup.Id);
        Assert.Equal("c1", Assert.IsType<Condition>(d.RootGroup.Children[0]).Id);
        var or = Assert.IsType<ConditionGroup>(d.RootGroup.Children[1]);
        Assert.Equal(("g1", LogicalOperator.Or), (or.Id, or.Logic));
        var fieldCompare = Assert.IsType<Condition>(or.Children[0]);
        Assert.Equal((OperandKind.Field, "StartDate"), (fieldCompare.Value!.Kind, fieldCompare.Value.Field));
        Assert.Equal(OperandKind.List, Assert.IsType<Condition>(or.Children[1]).Value!.Kind);
        Assert.NotNull(Assert.IsType<Condition>(or.Children[2]).ValueTo);
        Assert.Equal(0.9, t.Confidence);

        Assert.Empty(new DefinitionValidator(TestCatalog.Create()).Validate(d));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{ "status": "ok", "rootEntity": "Employee", "logic": "And", "conditions": [] }""")]
    [InlineData("""{ "status": "ok", "rootEntity": "Employee", "logic": "Xor", "conditions": [ { "field": "Salary", "operator": "IsEmpty" } ] }""")]
    [InlineData("""{ "status": "ok", "rootEntity": "Employee", "logic": "And", "conditions": [ { "field": "Salary", "operator": "DROP" } ] }""")]
    [InlineData("""{ "status": "ok", "rootEntity": "Employee", "logic": "And", "sql": "DELETE FROM hr.Employees", "conditions": [ { "field": "Salary", "operator": "IsEmpty" } ] }""")]
    [InlineData("""{ "status": "ok", "rootEntity": "Employee", "logic": "And", "conditions": [ { "field": "Salary", "operator": "IsEmpty", "rawSql": "1=1" } ] }""")]
    public void Malformed_or_unexpected_responses_produce_errors_not_definitions(string raw)
    {
        var t = AiResponseTranslator.Translate(raw);
        Assert.Null(t.Definition);
        Assert.NotEmpty(t.Errors);
    }

    [Fact]
    public void Unsupported_status_is_reported_with_notes()
    {
        var t = AiResponseTranslator.Translate("""{ "status": "unsupported", "rootEntity": "", "logic": "And", "conditions": [], "notes": ["לא הובן"] }""");
        Assert.Null(t.Definition);
        Assert.Equal(["לא הובן"], t.Notes);
    }

    [Fact]
    public void Context_contains_business_labels_operators_and_schema_but_no_physical_names()
    {
        var context = AiContextBuilder.Build(TestCatalog.Create(), "Employee");
        var entity = Assert.Single(context.Entities);
        Assert.Contains(entity.Fields, f => f.Path == "Department.IsActive" && f.Label == "מחלקה › סטטוס מחלקה");
        Assert.Contains("IsEmpty", entity.Fields.First(f => f.Path == "Email").Operators);
        Assert.DoesNotContain("Contains", entity.Fields.First(f => f.Path == "Salary").Operators);
        Assert.Equal("object", context.ResponseSchema.GetProperty("type").GetString());

        var json = System.Text.Json.JsonSerializer.Serialize(context);
        Assert.DoesNotContain("\"hr\"", json);
        Assert.DoesNotContain("Employees", json);
    }
}

public class AiProposalServiceTests
{
    private sealed class FakeCatalog : IMetadataCatalogProvider
    {
        public Task<MetadataCatalog> GetCatalogAsync(CancellationToken ct = default) => Task.FromResult(TestCatalog.Create());
        public void Invalidate() { }
    }

    private sealed class FakeProvider(string response) : IAiValidationProvider
    {
        public AiRequest? LastRequest { get; private set; }
        public string Name => "fake";
        public Task<string> GenerateAsync(AiRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    private static AiProposalService Service(IAiValidationProvider provider) =>
        new(new FakeCatalog(), provider, NullLogger<AiProposalService>.Instance);

    [Fact]
    public async Task Valid_response_becomes_validated_proposal_with_hebrew_rule_text()
    {
        var provider = new FakeProvider("""
        { "status": "ok", "name": "קטינים", "rootEntity": "Employee", "logic": "And",
          "conditions": [ { "field": "Age", "operator": "LessThan", "value": 18 },
                          { "field": "Salary", "operator": "GreaterThan", "value": 15000 } ] }
        """);

        var proposal = await Service(provider).ProposeAsync("עובדים מתחת לגיל 18 עם שכר מעל 15000", null);

        Assert.True(proposal.IsValid);
        Assert.Equal("קטינים", proposal.Name);
        Assert.Equal("גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000", proposal.RuleText);
        Assert.Equal(2, proposal.Conditions.Count);
        Assert.NotNull(proposal.Definition);
        Assert.Equal("עובדים מתחת לגיל 18 עם שכר מעל 15000", provider.LastRequest!.Prompt);
        Assert.NotEmpty(provider.LastRequest.Context.Entities);
    }

    [Fact]
    public async Task Response_referencing_unknown_fields_is_rejected_by_the_regular_validator()
    {
        var provider = new FakeProvider("""
        { "status": "ok", "rootEntity": "Employee", "logic": "And",
          "conditions": [ { "field": "Password", "operator": "IsEmpty" },
                          { "field": "Salary", "operator": "Contains", "value": "1" } ] }
        """);

        var proposal = await Service(provider).ProposeAsync("משהו", null);

        Assert.False(proposal.IsValid);
        Assert.Equal(2, proposal.Errors.Count);
        Assert.Null(proposal.RuleText);
    }

    [Fact]
    public async Task Provider_failure_becomes_ai_provider_exception()
    {
        var failing = new FailingProvider();
        await Assert.ThrowsAsync<AiProviderException>(() => Service(failing).ProposeAsync("בדיקה", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_prompt_is_rejected(string prompt)
    {
        await Assert.ThrowsAsync<InvalidDefinitionException>(() => Service(new FakeProvider("{}")).ProposeAsync(prompt, null));
    }

    private sealed class FailingProvider : IAiValidationProvider
    {
        public string Name => "failing";
        public Task<string> GenerateAsync(AiRequest request, CancellationToken ct = default) => throw new HttpRequestException("down");
    }
}
