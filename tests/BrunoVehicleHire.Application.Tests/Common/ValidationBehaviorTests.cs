using BrunoVehicleHire.Application.Common;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using NSubstitute;

namespace BrunoVehicleHire.Application.Tests.Common;

/// <summary>
/// Proves <see cref="ValidationBehavior{TRequest,TResponse}"/> runs every registered
/// <see cref="IValidator{T}"/> for the request, aggregates every <see cref="ValidationFailure"/>
/// across all of them, and throws <see cref="ValidationException"/> BEFORE calling
/// <c>next()</c> when any failures exist -- short-circuiting the handler entirely. When validation
/// succeeds, the pipeline passes through cleanly and <c>next()</c> is invoked exactly once.
/// </summary>
public class ValidationBehaviorTests
{
    public record FakeRequest(string Value) : IRequest<string>;

    [Fact]
    public async Task Handle_ValidationFails_ThrowsValidationException_AndNeverCallsNext()
    {
        var failingValidator = Substitute.For<IValidator<FakeRequest>>();
        failingValidator.ValidateAsync(Arg.Any<ValidationContext<FakeRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[] { new ValidationFailure("Value", "Value is required.") }));

        var behavior = new ValidationBehavior<FakeRequest, string>(new[] { failingValidator });

        var nextCalled = false;
        RequestHandlerDelegate<string> next = (_) =>
        {
            nextCalled = true;
            return Task.FromResult("should never be reached");
        };

        var act = async () => await behavior.Handle(new FakeRequest("irrelevant"), next, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidationFails_AggregatesFailuresAcrossAllValidators()
    {
        var validatorOne = Substitute.For<IValidator<FakeRequest>>();
        validatorOne.ValidateAsync(Arg.Any<ValidationContext<FakeRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[] { new ValidationFailure("Value", "First failure.") }));

        var validatorTwo = Substitute.For<IValidator<FakeRequest>>();
        validatorTwo.ValidateAsync(Arg.Any<ValidationContext<FakeRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[] { new ValidationFailure("Other", "Second failure.") }));

        var behavior = new ValidationBehavior<FakeRequest, string>(new[] { validatorOne, validatorTwo });

        RequestHandlerDelegate<string> next = (_) => Task.FromResult("unreached");

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new FakeRequest("irrelevant"), next, CancellationToken.None));

        exception.Errors.Should().HaveCount(2);
        exception.Errors.Should().Contain(f => f.PropertyName == "Value" && f.ErrorMessage == "First failure.");
        exception.Errors.Should().Contain(f => f.PropertyName == "Other" && f.ErrorMessage == "Second failure.");
    }

    [Fact]
    public async Task Handle_ValidationSucceeds_CallsNext_AndReturnsItsResult()
    {
        var passingValidator = Substitute.For<IValidator<FakeRequest>>();
        passingValidator.ValidateAsync(Arg.Any<ValidationContext<FakeRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var behavior = new ValidationBehavior<FakeRequest, string>(new[] { passingValidator });

        RequestHandlerDelegate<string> next = (_) => Task.FromResult("handler result");

        var result = await behavior.Handle(new FakeRequest("irrelevant"), next, CancellationToken.None);

        result.Should().Be("handler result");
    }

    [Fact]
    public async Task Handle_NoValidatorsRegistered_CallsNext()
    {
        var behavior = new ValidationBehavior<FakeRequest, string>(Array.Empty<IValidator<FakeRequest>>());

        RequestHandlerDelegate<string> next = (_) => Task.FromResult("handler result");

        var result = await behavior.Handle(new FakeRequest("irrelevant"), next, CancellationToken.None);

        result.Should().Be("handler result");
    }
}
