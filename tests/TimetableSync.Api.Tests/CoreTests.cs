using Xunit;
using FluentAssertions;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Tests;

public class CoreTests
{
    [Fact]
    public void SimpleTest()
    {
        true.Should().BeTrue();
    }

    [Fact]
    public void ParserCanBeInstantiated()
    {
        var parser = new TimetableParser();
        parser.Should().NotBeNull();
    }
}
