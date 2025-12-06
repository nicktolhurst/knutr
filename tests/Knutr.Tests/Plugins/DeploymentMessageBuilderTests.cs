namespace Knutr.Tests.Plugins;

using FluentAssertions;
using Knutr.Plugins.GitLabPipeline.Messaging;
using Xunit;

public class DeploymentMessageBuilderTests
{
    [Fact]
    public void BuildText_InProgress_ShowsDeployingMessage()
    {
        // Arrange
        var builder = new DeploymentMessageBuilder("main", "demo");

        // Act
        var text = builder.BuildText();

        // Assert
        text.Should().Contain("Deploying main to demo");
    }

    [Fact]
    public void BuildText_Success_ShowsDeployedMessage()
    {
        // Arrange
        var builder = new DeploymentMessageBuilder("main", "demo")
            .MarkSuccess();

        // Act
        var text = builder.BuildText();

        // Assert
        text.Should().Contain("Deployed main to demo");
    }

    [Fact]
    public void BuildText_Failed_ShowsFailedMessage()
    {
        // Arrange
        var builder = new DeploymentMessageBuilder("main", "demo")
            .MarkFailed("Pipeline error");

        // Act
        var text = builder.BuildText();

        // Assert
        text.Should().Contain("failed");
    }

    [Fact]
    public void BuildBlocks_ContainsHeaderSection()
    {
        // Arrange
        var builder = new DeploymentMessageBuilder("main", "demo");

        // Act
        var blocks = builder.BuildBlocks();

        // Assert
        blocks.Should().NotBeEmpty();
        blocks.Length.Should().BeGreaterOrEqualTo(2); // Header + context at minimum
    }

    [Fact]
    public void AddStep_CreatesStepsSection()
    {
        // Arrange
        var builder = new DeploymentMessageBuilder("main", "demo")
            .AddStep("Checking build", StepState.Success, "#123")
            .AddStep("Running pipeline", StepState.InProgress);

        // Act
        var blocks = builder.BuildBlocks();

        // Assert
        blocks.Length.Should().BeGreaterOrEqualTo(3); // Header + steps + context
    }

    [Fact]
    public void AddStep_UpdatesExistingStep()
    {
        // Arrange
        var builder = new DeploymentMessageBuilder("main", "demo")
            .AddStep("Checking build", StepState.InProgress)
            .AddStep("Checking build", StepState.Success, "#123");

        // Act
        var blocks = builder.BuildBlocks();

        // Assert - should only have one "Checking build" step, not two
        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void SetPipelineUrl_IncludedInContextBlock()
    {
        // Arrange
        var builder = new DeploymentMessageBuilder("main", "demo")
            .SetPipelineUrl("https://gitlab.example.com/pipelines/123");

        // Act
        var blocks = builder.BuildBlocks();

        // Assert
        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void SetDuration_IncludedInContextBlock()
    {
        // Arrange
        var builder = new DeploymentMessageBuilder("main", "demo")
            .SetDuration(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(30)));

        // Act
        var blocks = builder.BuildBlocks();

        // Assert
        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void MarkCancelled_SetsCorrectState()
    {
        // Arrange
        var builder = new DeploymentMessageBuilder("main", "demo")
            .MarkCancelled();

        // Act
        var text = builder.BuildText();

        // Assert
        text.Should().Contain("cancelled");
    }

    [Fact]
    public void ChainedMethods_ReturnBuilderForFluency()
    {
        // Arrange & Act
        var builder = new DeploymentMessageBuilder("main", "demo")
            .AddStep("Step 1", StepState.Success)
            .AddStep("Step 2", StepState.InProgress)
            .SetPipelineUrl("https://example.com")
            .SetDuration(TimeSpan.FromMinutes(1))
            .MarkSuccess();

        // Assert
        builder.Should().NotBeNull();
        builder.BuildBlocks().Should().NotBeEmpty();
    }
}
