using FluentAssertions;
using WpfDevTools.Shared.Security;
using Xunit;

namespace WpfDevTools.Tests.Unit.Security;

public class ChallengeGeneratorTests
{
    [Fact]
    public void GenerateChallenge_ShouldReturn32Bytes()
    {
        // Arrange
        var generator = new ChallengeGenerator();

        // Act
        var challenge = generator.GenerateChallenge();

        // Assert
        challenge.Should().HaveCount(32);
    }

    [Fact]
    public void GenerateChallenge_ShouldReturnDifferentValuesEachTime()
    {
        // Arrange
        var generator = new ChallengeGenerator();

        // Act
        var challenge1 = generator.GenerateChallenge();
        var challenge2 = generator.GenerateChallenge();

        // Assert
        challenge1.Should().NotEqual(challenge2);
    }

    [Fact]
    public void GenerateChallenge_ShouldNotReturnAllZeros()
    {
        // Arrange
        var generator = new ChallengeGenerator();

        // Act
        var challenge = generator.GenerateChallenge();

        // Assert
        challenge.Should().Contain(b => b != 0, "challenge should contain at least one non-zero byte");
    }

    [Fact]
    public void GenerateChallenge_CalledMultipleTimes_ShouldProduceUniqueValues()
    {
        // Arrange
        var generator = new ChallengeGenerator();
        var challenges = new HashSet<string>();

        // Act - Generate 100 challenges
        for (int i = 0; i < 100; i++)
        {
            var challenge = generator.GenerateChallenge();
            var challengeString = Convert.ToBase64String(challenge);
            challenges.Add(challengeString);
        }

        // Assert - All should be unique
        challenges.Should().HaveCount(100, "all 100 challenges should be unique");
    }

    [Fact]
    public void GenerateChallenge_ShouldBeThreadSafe()
    {
        // Arrange
        var generator = new ChallengeGenerator();
        var challenges = new System.Collections.Concurrent.ConcurrentBag<byte[]>();

        // Act - Generate challenges from multiple threads
        Parallel.For(0, 100, _ =>
        {
            var challenge = generator.GenerateChallenge();
            challenges.Add(challenge);
        });

        // Assert
        challenges.Should().HaveCount(100);
        challenges.Should().OnlyContain(c => c.Length == 32);

        // Check uniqueness
        var uniqueChallenges = challenges
            .Select(c => Convert.ToBase64String(c))
            .Distinct()
            .Count();
        uniqueChallenges.Should().Be(100, "all challenges should be unique even when generated concurrently");
    }
}
