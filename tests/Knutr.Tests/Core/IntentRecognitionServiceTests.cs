namespace Knutr.Tests.Core;

using FluentAssertions;
using Knutr.Core.Intent;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class IntentRecognitionServiceTests
{
    private readonly IntentRecognitionService _sut;

    public IntentRecognitionServiceTests()
    {
        _sut = new IntentRecognitionService(NullLogger<IntentRecognitionService>.Instance);
    }

    #region Deploy Intent Tests

    [Theory]
    [InlineData("deploy main to demo", "main", "demo")]
    [InlineData("deploy feature/xyz to production", "feature/xyz", "production")]
    [InlineData("deploy develop to staging", "develop", "staging")]
    [InlineData("please deploy main to demo", "main", "demo")]
    [InlineData("deploy the main branch to demo", "main", "demo")]
    public async Task RecognizeAsync_DeployWithEnvironment_ReturnsDeployIntent(
        string input, string expectedBranch, string expectedEnv)
    {
        // Act
        var result = await _sut.RecognizeAsync(input);

        // Assert
        result.HasIntent.Should().BeTrue();
        result.Command.Should().Be("gitlab");
        result.Action.Should().Be("deploy");
        result.Parameters["branch"].Should().Be(expectedBranch);
        result.Parameters["env"].Should().Be(expectedEnv);
    }

    [Theory]
    [InlineData("deploy main", "main")]
    [InlineData("deploy feature/test", "feature/test")]
    [InlineData("deploy develop branch", "develop")]
    public async Task RecognizeAsync_DeployWithoutEnvironment_ReturnsDeployIntent(
        string input, string expectedBranch)
    {
        // Act
        var result = await _sut.RecognizeAsync(input);

        // Assert
        result.HasIntent.Should().BeTrue();
        result.Command.Should().Be("gitlab");
        result.Action.Should().Be("deploy");
        result.Parameters["branch"].Should().Be(expectedBranch);
    }

    #endregion

    #region Build Intent Tests

    [Theory]
    [InlineData("build main", "main")]
    [InlineData("build feature/new-feature", "feature/new-feature")]
    [InlineData("build the develop branch", "develop")]
    [InlineData("run build on main", "main")]
    [InlineData("please build main", "main")]
    public async Task RecognizeAsync_Build_ReturnsBuildIntent(string input, string expectedBranch)
    {
        // Act
        var result = await _sut.RecognizeAsync(input);

        // Assert
        result.HasIntent.Should().BeTrue();
        result.Command.Should().Be("gitlab");
        result.Action.Should().Be("build");
        result.Parameters["branch"].Should().Be(expectedBranch);
    }

    #endregion

    #region Status Intent Tests

    [Theory]
    [InlineData("status")]
    [InlineData("pipeline status")]
    [InlineData("check status")]
    [InlineData("show pipelines")]
    [InlineData("show pipeline status")]
    public async Task RecognizeAsync_Status_ReturnsStatusIntent(string input)
    {
        // Act
        var result = await _sut.RecognizeAsync(input);

        // Assert
        result.HasIntent.Should().BeTrue();
        result.Command.Should().Be("gitlab");
        result.Action.Should().Be("status");
    }

    #endregion

    #region Cancel Intent Tests

    [Theory]
    [InlineData("cancel", null)]
    [InlineData("cancel pipeline", null)]
    [InlineData("cancel pipeline 123", "123")]
    [InlineData("stop pipeline", null)]
    [InlineData("abort build", null)]
    public async Task RecognizeAsync_Cancel_ReturnsCancelIntent(string input, string? expectedId)
    {
        // Act
        var result = await _sut.RecognizeAsync(input);

        // Assert
        result.HasIntent.Should().BeTrue();
        result.Command.Should().Be("gitlab");
        result.Action.Should().Be("cancel");
        if (expectedId != null)
        {
            result.Parameters["id"].Should().Be(expectedId);
        }
    }

    #endregion

    #region Retry Intent Tests

    [Theory]
    [InlineData("retry", null)]
    [InlineData("retry pipeline", null)]
    [InlineData("retry pipeline 456", "456")]
    [InlineData("retry the last build", null)]
    public async Task RecognizeAsync_Retry_ReturnsRetryIntent(string input, string? expectedId)
    {
        // Act
        var result = await _sut.RecognizeAsync(input);

        // Assert
        result.HasIntent.Should().BeTrue();
        result.Command.Should().Be("gitlab");
        result.Action.Should().Be("retry");
        if (expectedId != null)
        {
            result.Parameters["id"].Should().Be(expectedId);
        }
    }

    #endregion

    #region No Intent Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello there")]
    [InlineData("what is the weather today")]
    [InlineData("tell me a joke")]
    public async Task RecognizeAsync_UnrecognizedInput_ReturnsNoIntent(string input)
    {
        // Act
        var result = await _sut.RecognizeAsync(input);

        // Assert
        result.HasIntent.Should().BeFalse();
        result.Should().Be(Knutr.Abstractions.Intent.IntentResult.None);
    }

    #endregion

    #region Confidence Tests

    [Fact]
    public async Task RecognizeAsync_DeployWithEnv_HasHighConfidence()
    {
        // Act
        var result = await _sut.RecognizeAsync("deploy main to demo");

        // Assert
        result.Confidence.Should().BeGreaterOrEqualTo(0.85f);
    }

    [Fact]
    public async Task RecognizeAsync_Status_HasHighConfidence()
    {
        // Act
        var result = await _sut.RecognizeAsync("status");

        // Assert
        result.Confidence.Should().BeGreaterOrEqualTo(0.85f);
    }

    #endregion

    #region IntentResult Static Factory Tests

    [Fact]
    public void IntentResult_Deploy_CreatesCorrectIntent()
    {
        // Act
        var result = Knutr.Abstractions.Intent.IntentResult.Deploy("feature/test", "staging", 0.95f);

        // Assert
        result.Command.Should().Be("gitlab");
        result.Action.Should().Be("deploy");
        result.Parameters["branch"].Should().Be("feature/test");
        result.Parameters["env"].Should().Be("staging");
        result.Confidence.Should().Be(0.95f);
        result.HasIntent.Should().BeTrue();
    }

    [Fact]
    public void IntentResult_Build_CreatesCorrectIntent()
    {
        // Act
        var result = Knutr.Abstractions.Intent.IntentResult.Build("main", 0.9f);

        // Assert
        result.Command.Should().Be("gitlab");
        result.Action.Should().Be("build");
        result.Parameters["branch"].Should().Be("main");
        result.Confidence.Should().Be(0.9f);
    }

    [Fact]
    public void IntentResult_None_HasNoIntent()
    {
        // Act
        var result = Knutr.Abstractions.Intent.IntentResult.None;

        // Assert
        result.HasIntent.Should().BeFalse();
        result.Command.Should().BeNull();
        result.Action.Should().BeNull();
        result.Confidence.Should().Be(0f);
    }

    #endregion
}
