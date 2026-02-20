using FluentValidation;

namespace DotNetDBTasks.Application.Features.QueryExecution.Commands;

public class ExecuteQueryValidator : AbstractValidator<ExecuteQueryCommand>
{
    public ExecuteQueryValidator()
    {
        RuleFor(x => x.QueryId).NotEmpty();
    }
}
