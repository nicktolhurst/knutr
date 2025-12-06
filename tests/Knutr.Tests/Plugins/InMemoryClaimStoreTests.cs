namespace Knutr.Tests.Plugins;

using FluentAssertions;
using Knutr.Plugins.EnvironmentClaim;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class InMemoryClaimStoreTests
{
    private readonly InMemoryClaimStore _sut;

    public InMemoryClaimStoreTests()
    {
        _sut = new InMemoryClaimStore(NullLogger<InMemoryClaimStore>.Instance);
    }

    #region TryClaim Tests

    [Fact]
    public void TryClaim_UnclaimedEnvironment_Succeeds()
    {
        // Act
        var result = _sut.TryClaim("dev", "U123", "testing feature");

        // Assert
        result.Success.Should().BeTrue();
        result.Claim.Should().NotBeNull();
        result.Claim!.Environment.Should().Be("dev");
        result.Claim.UserId.Should().Be("U123");
        result.Claim.Note.Should().Be("testing feature");
        result.Claim.Status.Should().Be(ClaimStatus.Claimed);
    }

    [Fact]
    public void TryClaim_SameUserClaimsTwice_ReturnsSuccessWithMessage()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var result = _sut.TryClaim("dev", "U123");

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().Contain("already");
    }

    [Fact]
    public void TryClaim_DifferentUserClaimsOccupied_Fails()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var result = _sut.TryClaim("dev", "U456");

        // Assert
        result.Success.Should().BeFalse();
        result.BlockedByUserId.Should().Be("U123");
        result.ErrorMessage.Should().Contain("claimed by another user");
    }

    [Fact]
    public void TryClaim_CaseInsensitiveEnvironment()
    {
        // Arrange
        _sut.TryClaim("DEV", "U123");

        // Act
        var result = _sut.TryClaim("dev", "U456");

        // Assert
        result.Success.Should().BeFalse();
        result.BlockedByUserId.Should().Be("U123");
    }

    [Fact]
    public void TryClaim_SetsClaimedAtTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var result = _sut.TryClaim("dev", "U123");

        // Assert
        var after = DateTime.UtcNow;
        result.Claim!.ClaimedAt.Should().BeOnOrAfter(before);
        result.Claim.ClaimedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void TryClaim_NullNote_Succeeds()
    {
        // Act
        var result = _sut.TryClaim("dev", "U123", null);

        // Assert
        result.Success.Should().BeTrue();
        result.Claim!.Note.Should().BeNull();
    }

    #endregion

    #region Get Tests

    [Fact]
    public void Get_ExistingClaim_ReturnsClaim()
    {
        // Arrange
        _sut.TryClaim("dev", "U123", "my note");

        // Act
        var claim = _sut.Get("dev");

        // Assert
        claim.Should().NotBeNull();
        claim!.UserId.Should().Be("U123");
        claim.Note.Should().Be("my note");
    }

    [Fact]
    public void Get_NonExistentEnvironment_ReturnsNull()
    {
        // Act
        var claim = _sut.Get("nonexistent");

        // Assert
        claim.Should().BeNull();
    }

    [Fact]
    public void Get_CaseInsensitive()
    {
        // Arrange
        _sut.TryClaim("DEV", "U123");

        // Act
        var claim = _sut.Get("dev");

        // Assert
        claim.Should().NotBeNull();
    }

    #endregion

    #region GetAll Tests

    [Fact]
    public void GetAll_EmptyStore_ReturnsEmptyList()
    {
        // Act
        var claims = _sut.GetAll();

        // Assert
        claims.Should().BeEmpty();
    }

    [Fact]
    public void GetAll_MultipleClaims_ReturnsAll()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");
        _sut.TryClaim("staging", "U456");
        _sut.TryClaim("production", "U789");

        // Act
        var claims = _sut.GetAll();

        // Assert
        claims.Should().HaveCount(3);
        claims.Select(c => c.Environment).Should().Contain(["dev", "staging", "production"]);
    }

    #endregion

    #region GetByUser Tests

    [Fact]
    public void GetByUser_UserWithClaims_ReturnsUserClaims()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");
        _sut.TryClaim("staging", "U123");
        _sut.TryClaim("production", "U456");

        // Act
        var claims = _sut.GetByUser("U123");

        // Assert
        claims.Should().HaveCount(2);
        claims.Should().AllSatisfy(c => c.UserId.Should().Be("U123"));
    }

    [Fact]
    public void GetByUser_UserWithNoClaims_ReturnsEmptyList()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var claims = _sut.GetByUser("U456");

        // Assert
        claims.Should().BeEmpty();
    }

    #endregion

    #region GetStale Tests

    [Fact]
    public void GetStale_NoStaleClaims_ReturnsEmpty()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var stale = _sut.GetStale(TimeSpan.FromHours(1));

        // Assert
        stale.Should().BeEmpty();
    }

    [Fact]
    public void GetStale_WithStaleClaims_ReturnsStaleClaims()
    {
        // This test is tricky since we can't easily manipulate time
        // We'll just verify the method runs without error
        _sut.TryClaim("dev", "U123");

        // With zero timespan, all claims are stale
        var stale = _sut.GetStale(TimeSpan.Zero);

        stale.Should().HaveCount(1);
    }

    #endregion

    #region Release Tests

    [Fact]
    public void Release_OwnerReleases_Succeeds()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var released = _sut.Release("dev", "U123");

        // Assert
        released.Should().BeTrue();
        _sut.Get("dev").Should().BeNull();
    }

    [Fact]
    public void Release_NonOwnerReleases_Fails()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var released = _sut.Release("dev", "U456");

        // Assert
        released.Should().BeFalse();
        _sut.Get("dev").Should().NotBeNull();
    }

    [Fact]
    public void Release_NonOwnerWithForce_Succeeds()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var released = _sut.Release("dev", "U456", force: true);

        // Assert
        released.Should().BeTrue();
        _sut.Get("dev").Should().BeNull();
    }

    [Fact]
    public void Release_NonExistentEnvironment_ReturnsFalse()
    {
        // Act
        var released = _sut.Release("nonexistent", "U123");

        // Assert
        released.Should().BeFalse();
    }

    [Fact]
    public void Release_CaseInsensitive()
    {
        // Arrange
        _sut.TryClaim("DEV", "U123");

        // Act
        var released = _sut.Release("dev", "U123");

        // Assert
        released.Should().BeTrue();
    }

    #endregion

    #region Transfer Tests

    [Fact]
    public void Transfer_ValidTransfer_Succeeds()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var transferred = _sut.Transfer("dev", "U123", "U456");

        // Assert
        transferred.Should().BeTrue();
        var claim = _sut.Get("dev");
        claim!.UserId.Should().Be("U456");
        claim.Note.Should().Contain("Transferred from");
    }

    [Fact]
    public void Transfer_WrongOwner_Fails()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var transferred = _sut.Transfer("dev", "U456", "U789");

        // Assert
        transferred.Should().BeFalse();
        _sut.Get("dev")!.UserId.Should().Be("U123");
    }

    [Fact]
    public void Transfer_NonExistentEnvironment_Fails()
    {
        // Act
        var transferred = _sut.Transfer("nonexistent", "U123", "U456");

        // Assert
        transferred.Should().BeFalse();
    }

    [Fact]
    public void Transfer_ResetsClaimedAt()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");
        var originalClaim = _sut.Get("dev");
        Thread.Sleep(10);

        // Act
        _sut.Transfer("dev", "U123", "U456");

        // Assert
        var newClaim = _sut.Get("dev");
        newClaim!.ClaimedAt.Should().BeAfter(originalClaim!.ClaimedAt);
    }

    #endregion

    #region UpdateStatus Tests

    [Fact]
    public void UpdateStatus_ExistingClaim_UpdatesStatus()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var updated = _sut.UpdateStatus("dev", ClaimStatus.Deploying);

        // Assert
        updated.Should().BeTrue();
        _sut.Get("dev")!.Status.Should().Be(ClaimStatus.Deploying);
    }

    [Fact]
    public void UpdateStatus_NonExistentEnvironment_ReturnsFalse()
    {
        // Act
        var updated = _sut.UpdateStatus("nonexistent", ClaimStatus.Deploying);

        // Assert
        updated.Should().BeFalse();
    }

    [Theory]
    [InlineData(ClaimStatus.Claimed)]
    [InlineData(ClaimStatus.Deploying)]
    [InlineData(ClaimStatus.Nudged)]
    [InlineData(ClaimStatus.PendingTransfer)]
    public void UpdateStatus_AllStatuses_Work(ClaimStatus status)
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        _sut.UpdateStatus("dev", status);

        // Assert
        _sut.Get("dev")!.Status.Should().Be(status);
    }

    #endregion

    #region RecordActivity Tests

    [Fact]
    public void RecordActivity_ExistingClaim_UpdatesLastActivityAt()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");
        var originalActivity = _sut.Get("dev")!.LastActivityAt;
        Thread.Sleep(10);

        // Act
        var recorded = _sut.RecordActivity("dev");

        // Assert
        recorded.Should().BeTrue();
        _sut.Get("dev")!.LastActivityAt.Should().BeAfter(originalActivity!.Value);
    }

    [Fact]
    public void RecordActivity_NonExistentEnvironment_ReturnsFalse()
    {
        // Act
        var recorded = _sut.RecordActivity("nonexistent");

        // Assert
        recorded.Should().BeFalse();
    }

    #endregion

    #region RecordNudge Tests

    [Fact]
    public void RecordNudge_ExistingClaim_IncrementsCountAndUpdatesTimestamp()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var recorded = _sut.RecordNudge("dev");

        // Assert
        recorded.Should().BeTrue();
        var claim = _sut.Get("dev")!;
        claim.NudgeCount.Should().Be(1);
        claim.LastNudgedAt.Should().NotBeNull();
        claim.Status.Should().Be(ClaimStatus.Nudged);
    }

    [Fact]
    public void RecordNudge_MultipleTimes_IncrementsEachTime()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        _sut.RecordNudge("dev");
        _sut.RecordNudge("dev");
        _sut.RecordNudge("dev");

        // Assert
        _sut.Get("dev")!.NudgeCount.Should().Be(3);
    }

    [Fact]
    public void RecordNudge_NonExistentEnvironment_ReturnsFalse()
    {
        // Act
        var recorded = _sut.RecordNudge("nonexistent");

        // Assert
        recorded.Should().BeFalse();
    }

    #endregion

    #region IsAvailable Tests

    [Fact]
    public void IsAvailable_UnclaimedEnvironment_ReturnsTrue()
    {
        // Act
        var available = _sut.IsAvailable("dev", "U123");

        // Assert
        available.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_ClaimedBySameUser_ReturnsTrue()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var available = _sut.IsAvailable("dev", "U123");

        // Assert
        available.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_ClaimedByDifferentUser_ReturnsFalse()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        // Act
        var available = _sut.IsAvailable("dev", "U456");

        // Assert
        available.Should().BeFalse();
    }

    #endregion

    #region GetAvailableEnvironments Tests

    [Fact]
    public void GetAvailableEnvironments_AllUnclaimed_ReturnsAll()
    {
        // Arrange
        var allEnvs = new[] { "dev", "staging", "production" };

        // Act
        var available = _sut.GetAvailableEnvironments("U123", allEnvs);

        // Assert
        available.Should().BeEquivalentTo(allEnvs);
    }

    [Fact]
    public void GetAvailableEnvironments_SomeClaimed_ReturnsUnclaimedAndOwned()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");
        _sut.TryClaim("staging", "U456");
        var allEnvs = new[] { "dev", "staging", "production" };

        // Act
        var available = _sut.GetAvailableEnvironments("U123", allEnvs);

        // Assert
        available.Should().BeEquivalentTo(["dev", "production"]);
    }

    [Fact]
    public void GetAvailableEnvironments_AllClaimedByOthers_ReturnsEmpty()
    {
        // Arrange
        _sut.TryClaim("dev", "U456");
        _sut.TryClaim("staging", "U456");
        var allEnvs = new[] { "dev", "staging" };

        // Act
        var available = _sut.GetAvailableEnvironments("U123", allEnvs);

        // Assert
        available.Should().BeEmpty();
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task TryClaim_ConcurrentClaims_OnlyOneSucceeds()
    {
        // Arrange
        var successCount = 0;
        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() =>
            {
                var result = _sut.TryClaim("dev", $"U{i}");
                if (result.Success && result.Claim?.UserId == $"U{i}")
                {
                    Interlocked.Increment(ref successCount);
                }
            }));

        // Act
        await Task.WhenAll(tasks);

        // Assert
        successCount.Should().Be(1);
        _sut.Get("dev").Should().NotBeNull();
    }

    [Fact]
    public async Task Operations_ThreadSafe_MixedOperations()
    {
        // Arrange
        _sut.TryClaim("dev", "U123");

        var tasks = new List<Task>();
        for (var i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                _sut.Get("dev");
                _sut.GetAll();
                _sut.IsAvailable("dev", $"U{index}");
                _sut.RecordActivity("dev");
            }));
        }

        // Act & Assert - should not throw
        await Task.WhenAll(tasks);
        _sut.Get("dev").Should().NotBeNull();
    }

    #endregion
}
