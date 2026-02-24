using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Assigns a dynamic query to one or more departments.
/// All users in those departments will gain access to the query.
/// </summary>
public class AssignQueryToDepartmentsCommand : IRequest<Unit>
{
    public Guid QueryId { get; set; }
    public List<string> Departments { get; set; } = new();
}

public class AssignQueryToDepartmentsCommandHandler : IRequestHandler<AssignQueryToDepartmentsCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignQueryToDepartmentsCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignQueryToDepartmentsCommand request, CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (query is null)
            throw new NotFoundException(nameof(DynamicQuery), request.QueryId);

        // Remove existing department assignments
        var existing = await _unitOfWork.DynamicQueryDepartments.FindAsync(
            qd => qd.DynamicQueryId == request.QueryId, cancellationToken);
        foreach (var qd in existing)
            _unitOfWork.DynamicQueryDepartments.Delete(qd);

        // Add new assignments
        foreach (var department in request.Departments)
        {
            await _unitOfWork.DynamicQueryDepartments.AddAsync(new DynamicQueryDepartment
            {
                DynamicQueryId = request.QueryId,
                Department = department
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
