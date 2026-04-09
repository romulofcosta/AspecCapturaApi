using FluentAssertions;

namespace AspecCapturaApi.Tests;

public class BasicTest
{
    [Fact]
    public void BasicTest_ShouldPass()
    {
        // Arrange
        var expected = 2;
        
        // Act
        var actual = 1 + 1;
        
        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void StringTest_ShouldWork()
    {
        // Arrange
        var text = "Hello World";
        
        // Act & Assert
        text.Should().Contain("World");
        text.Should().StartWith("Hello");
    }
}