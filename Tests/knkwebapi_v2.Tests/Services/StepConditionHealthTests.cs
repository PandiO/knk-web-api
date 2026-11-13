using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services
{
    /// <summary>
    /// PandiO/knk-workspace#88: the configuration health check flags a step condition whose rule reads a
    /// field that is not on the step (or not on an earlier step), as a warning.
    /// </summary>
    public class StepConditionHealthTests
    {
        private static FormStep Step(string name, string[] fields, params StepCondition[] conditions) => new()
        {
            StepName = name,
            Fields = fields.Select(f => new FormField { FieldName = f, FieldGuid = Guid.NewGuid() }).ToList(),
            StepConditions = conditions.ToList()
        };

        private static StepCondition Condition(string json, string type = "CompletionCondition", bool isActive = true) => new()
        {
            ConditionType = type,
            ConditionLogicJson = json,
            IsActive = isActive
        };

        private static FormConfiguration Config(params FormStep[] steps) => new() { Steps = steps.ToList() };

        [Fact]
        public void Condition_OnAFieldOfItsStep_IsHealthy()
        {
            var config = Config(Step("Region and access", new[] { "AllowEntry" },
                Condition("{\"conditions\":[{\"fieldName\":\"AllowEntry\",\"operator\":\"Equals\",\"value\":true}],\"logic\":\"AND\"}")));

            Assert.Empty(StepConditionHealth.FindIssues(config));
        }

        [Fact]
        public void Condition_OnAFieldThatIsNotOnTheStep_IsAWarning()
        {
            var config = Config(Step("Region and access", new[] { "AllowExit" },
                Condition("{\"conditions\":[{\"fieldName\":\"AllowEntry\",\"operator\":\"Equals\",\"value\":true}],\"logic\":\"AND\"}")));

            var issue = Assert.Single(StepConditionHealth.FindIssues(config));
            Assert.Equal("Warning", issue.Severity);
            Assert.Contains("Step 'Region and access'", issue.Message);
            Assert.Contains("\"before leaving\" condition reads the field 'AllowEntry', which is not on this step", issue.Message);
        }

        [Fact]
        public void FromPreviousStep_LooksAtEarlierStepsOnly()
        {
            const string nameNotEmpty = "{\"conditions\":[{\"fieldName\":\"Name\",\"operator\":\"IsNotEmpty\",\"fromPreviousStep\":true}],\"logic\":\"AND\"}";

            var healthy = Config(
                Step("Basics", new[] { "Name" }),
                Step("Details", new[] { "Notes" }, Condition(nameNotEmpty, "StartCondition")));
            Assert.Empty(StepConditionHealth.FindIssues(healthy));

            // The field is on the condition's own step (or a later one), not on an earlier one.
            var broken = Config(
                Step("Basics", new[] { "Motto" }),
                Step("Details", new[] { "Name" }, Condition(nameNotEmpty, "StartCondition")));
            var issue = Assert.Single(StepConditionHealth.FindIssues(broken));
            Assert.Contains("\"before entering\" condition reads the field 'Name', which is not on an earlier step", issue.Message);
        }

        [Fact]
        public void SwitchedOffCondition_IsOnlyInformation()
        {
            var config = Config(Step("Region", new[] { "Other" },
                Condition("{\"conditions\":[{\"fieldName\":\"Gone\",\"operator\":\"IsEmpty\"}]}", isActive: false)));

            var issue = Assert.Single(StepConditionHealth.FindIssues(config));
            Assert.Equal("Info", issue.Severity);
            Assert.Contains("switched off", issue.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{}")]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData("{\"field\":\"AgreedToTerms\",\"operator\":\"equals\",\"value\":true}")]
        [InlineData("{\"conditions\":\"x\"}")]
        public void JsonThatIsNotTheBuilderShape_IsNotJudged(string json)
        {
            var config = Config(Step("Region", new[] { "Other" }, Condition(json)));

            Assert.Empty(StepConditionHealth.FindIssues(config));
        }

        [Fact]
        public void EveryMissingField_GetsItsOwnIssue()
        {
            var config = Config(Step("Region", new[] { "Kept" }, Condition(
                "{\"conditions\":[{\"fieldName\":\"Kept\",\"operator\":\"IsNotEmpty\"},{\"fieldName\":\"A\",\"operator\":\"IsEmpty\"},{\"fieldName\":\"B\",\"operator\":\"IsEmpty\"}],\"logic\":\"OR\"}")));

            Assert.Equal(2, StepConditionHealth.FindIssues(config).Count);
        }
    }
}
