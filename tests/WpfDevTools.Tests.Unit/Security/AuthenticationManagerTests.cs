using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Shared.Security;
using Xunit;

namespace WpfDevTools.Tests.Unit.Security;

public class AuthenticationManagerTests
{
    [Fact]
    public void GetSharedSecret_ShouldReturn32Bytes()
    {
        // Arrange
        var manager = new AuthenticationManager();

        // Act
        var secret = manager.GetSharedSecret();

        // Assert
        secret.Should().HaveCount(32);
    }

    [Fact]
    public void GetSharedSecret_CalledMultipleTimes_ShouldReturnSameSecret()
    {
        // Arrange
        var manager = new AuthenticationManager();

        // Act
        var secret1 = manager.GetSharedSecret();
        var secret2 = manager.GetSharedSecret();

        // Assert
        secret1.Should().Equal(secret2);
    }

    [Fact]
    public void Constructor_WithEnvironmentVariable_ShouldUseProvidedSecret()
    {
        // Arrange
        var expectedSecret = new byte[32];
        RandomNumberGenerator.Fill(expectedSecret);
        var base64Secret = Convert.ToBase64String(expectedSecret);

        var manager = new AuthenticationManager(envSecretProvider: () => base64Secret);

        // Act
        var secret = manager.GetSharedSecret();

        // Assert
        secret.Should().Equal(expectedSecret);
    }

    [Fact]
    public void Constructor_WithNullEnvSecret_ShouldAutoGenerate()
    {
        // Arrange
        var manager = new AuthenticationManager(envSecretProvider: () => null);

        // Act
        var secret = manager.GetSharedSecret();

        // Assert
        secret.Should().HaveCount(32);
        secret.Should().Contain(b => b != 0, "auto-generated secret should not be all zeros");
    }

    [Fact]
    public void Constructor_WithEmptyEnvSecret_ShouldAutoGenerate()
    {
        // Arrange
        var manager = new AuthenticationManager(envSecretProvider: () => "");

        // Act
        var secret = manager.GetSharedSecret();

        // Assert
        secret.Should().HaveCount(32);
    }

    [Fact]
    public void Constructor_WithInvalidBase64EnvSecret_ShouldThrow()
    {
        // Arrange & Act
        var act = () => new AuthenticationManager(envSecretProvider: () => "not-valid-base64!!!");

        // Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Constructor_WithShortEnvSecret_ShouldThrow()
    {
        // Arrange - 16 bytes is too short (minimum 32)
        var shortSecret = new byte[16];
        RandomNumberGenerator.Fill(shortSecret);
        var base64Short = Convert.ToBase64String(shortSecret);

        // Act
        var act = () => new AuthenticationManager(envSecretProvider: () => base64Short);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void TwoDifferentManagers_WithSameSecret_ShouldProduceSameSecret()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var base64 = Convert.ToBase64String(secret);

        var manager1 = new AuthenticationManager(envSecretProvider: () => base64);
        var manager2 = new AuthenticationManager(envSecretProvider: () => base64);

        // Act & Assert
        manager1.GetSharedSecret().Should().Equal(manager2.GetSharedSecret());
    }

    [Fact]
    public void TwoDifferentManagers_WithAutoGenerate_ShouldProduceDifferentSecrets()
    {
        // Arrange
        var manager1 = new AuthenticationManager(envSecretProvider: () => null);
        var manager2 = new AuthenticationManager(envSecretProvider: () => null);

        // Act
        var secret1 = manager1.GetSharedSecret();
        var secret2 = manager2.GetSharedSecret();

        // Assert
        secret1.Should().NotEqual(secret2);
    }

    [Fact]
    public void IsAuthenticationEnabled_WithSecret_ShouldReturnTrue()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var manager = new AuthenticationManager(envSecretProvider: () => Convert.ToBase64String(secret));

        // Act & Assert
        manager.IsAuthenticationEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticationEnabled_WithAutoGenerate_ShouldReturnTrue()
    {
        // Arrange
        var manager = new AuthenticationManager(envSecretProvider: () => null);

        // Act & Assert
        manager.IsAuthenticationEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticationEnabled_WhenDisabled_ShouldReturnFalse()
    {
        // Arrange
        var manager = AuthenticationManager.CreateDisabled();

        // Act & Assert
        manager.IsAuthenticationEnabled.Should().BeFalse();
    }

    [Fact]
    public void CreateDisabled_GetSharedSecret_ShouldThrow()
    {
        // Arrange
        var manager = AuthenticationManager.CreateDisabled();

        // Act
        var act = () => manager.GetSharedSecret();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*disabled*");
    }

    [Fact]
    public void Dispose_ShouldPreventSubsequentGetSharedSecret()
    {
        // Arrange
        var manager = new AuthenticationManager();
        manager.GetSharedSecret(); // Verify it works before dispose

        // Act
        manager.Dispose();

        // Assert
        var act = () => manager.GetSharedSecret();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var manager = new AuthenticationManager();

        // Act & Assert
        var act = () =>
        {
            manager.Dispose();
            manager.Dispose();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_ShouldZeroSecretMemory()
    {
        // Arrange
        var knownSecret = new byte[32];
        RandomNumberGenerator.Fill(knownSecret);
        var base64 = Convert.ToBase64String(knownSecret);
        var manager = new AuthenticationManager(envSecretProvider: () => base64);

        // Verify secret is correct before dispose
        manager.GetSharedSecret().Should().Equal(knownSecret);

        // Act
        manager.Dispose();

        // Assert: After dispose, GetSharedSecret should throw (secret is zeroed)
        var act = () => manager.GetSharedSecret();
        act.Should().Throw<ObjectDisposedException>();
    }
}
