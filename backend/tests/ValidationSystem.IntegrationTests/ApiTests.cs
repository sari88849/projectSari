using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ValidationSystem.IntegrationTests;

/// <summary>HTTP-level tests of the REST API (real controllers, real database).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:ValidationDb", DatabaseFixture.ConnectionString);
}

[Collection(DatabaseCollection.Name)]
public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Metadata_endpoints_expose_hebrew_business_labels_only()
    {
        var entities = await _client.GetFromJsonAsync<JsonElement>("/api/metadata/entities");
        Assert.Contains(entities.EnumerateArray(), e => e.GetProperty("key").GetString() == "Employee"
                                                     && e.GetProperty("displayName").GetString() == "עובד");

        var fields = await _client.GetFromJsonAsync<JsonElement>("/api/metadata/entities/Employee/fields");
        var deptStatus = fields.EnumerateArray().Single(f => f.GetProperty("path").GetString() == "Department.IsActive");
        Assert.Equal("מחלקה › סטטוס מחלקה", deptStatus.GetProperty("label").GetString());
        Assert.DoesNotContain(fields.EnumerateArray(), f => f.ToString().Contains("hr.") || f.ToString().Contains("Employees"));

        var operators = await _client.GetFromJsonAsync<JsonElement>("/api/metadata/operators");
        Assert.Equal(12, operators.GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/metadata/entities/Nope/fields")).StatusCode);
    }

    [Fact]
    public async Task Create_execute_read_results_update_and_delete_a_validation()
    {
        var definition = JsonDocument.Parse("""
        { "rootEntity": "Employee",
          "condition": { "type": "group", "id": "root", "logic": "And", "children": [
            { "type": "condition", "id": "c1", "field": "Age", "operator": "LessThan", "value": { "kind": "constant", "value": 18 } } ] } }
        """).RootElement;

        var create = await _client.PostAsJsonAsync("/api/validations", new { name = "בדיקת API - קטינים", description = "נוצר בבדיקה", definition });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        try
        {
            var run = await (await _client.PostAsync($"/api/validations/{id}/execute", null)).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Succeeded", run.GetProperty("status").GetString());
            Assert.Equal(4, run.GetProperty("violationCount").GetInt32());   // E1013, E1014, E1015, E1016
            Assert.Contains(run.GetProperty("conditions").EnumerateArray(), c => c.GetProperty("description").GetString() == "גיל קטן מ-18");

            var runId = run.GetProperty("runId").GetInt64();
            var results = await _client.GetFromJsonAsync<JsonElement>($"/api/runs/{runId}/results?search=שירה");
            var item = Assert.Single(results.GetProperty("items").EnumerateArray());
            Assert.Equal("שירה קליין", item.GetProperty("recordTitle").GetString());
            Assert.StartsWith("גיל קטן מ-18 (ערך בפועל: 17)", item.GetProperty("reasons")[0].GetProperty("message").GetString());

            var update = await _client.PutAsJsonAsync($"/api/validations/{id}", new { name = "בדיקת API - עודכן", definition });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            var list = await _client.GetFromJsonAsync<JsonElement>("/api/validations?search=עודכן");
            var summary = Assert.Single(list.EnumerateArray());
            Assert.Equal("Succeeded", summary.GetProperty("lastRunStatus").GetString());
            Assert.Equal(4, summary.GetProperty("lastRunViolationCount").GetInt32());
        }
        finally
        {
            Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/validations/{id}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/validations/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("""{ "rootEntity": "Employee", "condition": { "type": "group", "id": "root", "logic": "And", "children": [ { "type": "condition", "id": "c1", "field": "Salary]; DROP TABLE hr.Employees;--", "operator": "IsEmpty" } ] } }""")]
    [InlineData("""{ "rootEntity": "Employee", "condition": { "type": "group", "id": "root", "logic": "And", "children": [ { "type": "condition", "id": "c1", "field": "Salary", "operator": "Contains", "value": { "kind": "constant", "value": "1" } } ] } }""")]
    [InlineData("""{ "rootEntity": "sys.tables", "condition": { "type": "group", "id": "root", "logic": "And", "children": [] } }""")]
    [InlineData("""{ "rootEntity": "Employee", "sql": "DELETE FROM hr.Employees", "condition": { "type": "group", "id": "root", "logic": "And", "children": [] } }""")]
    public async Task Invalid_definitions_are_rejected_with_400_and_hebrew_errors(string json)
    {
        var response = await _client.PostAsJsonAsync("/api/validations",
            new { name = "לא תקין", definition = JsonDocument.Parse(json).RootElement });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("הגדרת הבדיקה אינה תקינה", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("errors").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Missing_name_is_rejected()
    {
        var definition = JsonDocument.Parse("""
        { "rootEntity": "Employee", "condition": { "type": "group", "id": "root", "logic": "And", "children": [
            { "type": "condition", "id": "c1", "field": "Email", "operator": "IsEmpty" } ] } }
        """).RootElement;
        var response = await _client.PostAsJsonAsync("/api/validations", new { name = " ", definition });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Preview_returns_description_and_sql_without_executing()
    {
        var definition = JsonDocument.Parse("""
        { "rootEntity": "Employee", "condition": { "type": "group", "id": "root", "logic": "Or", "children": [
            { "type": "condition", "id": "c1", "field": "EndDate", "operator": "LessThan", "value": { "kind": "field", "field": "StartDate" } },
            { "type": "condition", "id": "c2", "field": "Department.IsActive", "operator": "Equals", "value": { "kind": "constant", "value": false } } ] } }
        """).RootElement;

        var preview = await (await _client.PostAsJsonAsync("/api/validations/preview", new { definition })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("isValid").GetBoolean());
        Assert.Equal("תאריך סיום העסקה מוקדם מ-\"תאריך תחילת העסקה\" או מחלקה › סטטוס מחלקה: לא פעילה",
                     preview.GetProperty("description").GetString());
        Assert.Contains("LEFT JOIN [hr].[Departments]", preview.GetProperty("generatedSql").GetString());
    }
}
