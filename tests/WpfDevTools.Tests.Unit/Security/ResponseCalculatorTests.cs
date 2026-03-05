using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Shared.Security;
using Xunit;

namespace WpfDevTools.Tests.Unit.Security;

public class ResponseCalculatorTests
{
    [Fact]
    public void ComputeResponse_WithSameChallenge_ShouldReturnSameResponse()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var calculator = new ResponseCalculator(secret);
        var challenge = new byte[32];
        RandomNumberGenerator.Fill(challenge);

        // Act
        var response1 = calculator.ComputeResponse(challenge);
        var response2 = calculator.ComputeResponse(challenge);

        // Assert
        response1.Should().Equal(response2);
    }

    [Fact]
    public void ComputeResponse_WithDifferentSecret_ShouldReturnDifferentResponse()
    {
        // Arrange
        var secret1 = new byte[32];
        var secret2 = new byte[32];
        RandomNumberGenerator.Fill(secret1);
        RandomNumberGenerator.Fill(secret2);

        var calculator1 = new ResponseCalculator(secret1);
        var calculator2 = new ResponseCalculator(secret2);

        var challenge = new byte[32];
        RandomNumberGenerator.Fill(challenge);

        // Act
        var response1 = calculator1.ComputeResponse(challenge);
        var response2 = calculator2.ComputeResponse(challenge);

        // Assert
        response1.Should().NotEqual(response2);
    }

    [Fact]
    public void ComputeResponse_ShouldReturn32Bytes()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var calculator = new ResponseCalculator(secret);
        var challenge = new byte[32];
        RandomNumberGenerator.Fill(challenge);

        // Act
        var response = calculator.ComputeResponse(challenge);

        // Assert
        response.Should().HaveCount(32);
    }

    [Fact]
    public void VerifyResponse_WithCorrectResponse_ShouldReturnTrue()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var calculator = new ResponseCalculator(secret);
        var challenge = new byte[32];
        RandomNumberGenerator.Fill(challenge);
        var correctResponse = calculator.ComputeResponse(challenge);

        // Act
        var result = calculator.VerifyResponse(challenge, correctResponse);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyResponse_WithWrongResponse_ShouldReturnFalse()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var calculator = new ResponseCalculator(secret);
        var challenge = new byte[32];
        RandomNumberGenerator.Fill(challenge);
        var wrongResponse = new byte[32];
        RandomNumberGenerator.Fill(wrongResponse);

        // Act
        var result = calculator.VerifyResponse(challenge, wrongResponse);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyResponse_WithModifiedResponse_ShouldReturnFalse()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var calculator = new ResponseCalculator(secret);
        var challenge = new byte[32];
        RandomNumberGenerator.Fill(challenge);
        var correctResponse = calculator.ComputeResponse(challenge);

        // Modify one byte
        var modifiedResponse = (byte[])correctResponse.Clone();
        modifiedResponse[0] ^= 0xFF;

        // Act
        var result = calculator.VerifyResponse(challenge, modifiedResponse);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithNullSecret_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new ResponseCalculator(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithEmptySecret_ShouldThrowArgumentException()
    {
        // Act
        var act = () => new ResponseCalculator(Array.Empty<byte>());

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeResponse_WithNullChallenge_ShouldThrowArgumentNullException()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var calculator = new ResponseCalculator(secret);

        // Act
        var act = () => calculator.ComputeResponse(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void VerifyResponse_WithNullChallenge_ShouldThrowArgumentNullException()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var calculator = new ResponseCalculator(secret);
        var response = new byte[32];

        // Act
        var act = () => calculator.VerifyResponse(null!, response);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void VerifyResponse_WithNullResponse_ShouldThrowArgumentNullException()
    {
        // Arrange
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var calculator = new ResponseCalculator(secret);
        var challenge = new byte[32];

        // Act
        var act = () => calculator.VerifyResponse(challenge, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
