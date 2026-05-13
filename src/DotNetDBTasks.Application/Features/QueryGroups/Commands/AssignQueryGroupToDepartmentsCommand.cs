using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryGroups.Commands;

public class AssignQueryGroupToDepartmentsCommand : IRequest<Unit>
{
    public Guid GroupId { get; set; }
    public List<string> Departments { get; set; } = new();
}

public class AssignQueryGroupToDepartmentsCommandHandler : IRequestHandler<AssignQueryGroupToDepartmentsCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignQueryGroupToDepartmentsCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignQueryGroupToDepartmentsCommand request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.QueryGroups.GetByIdAsync(request.GroupId, cancellationToken);
        if (group is null)
            throw new NotFoundException(nameof(QueryGroup), request.GroupId);

        var existing = await _unitOfWork.QueryGroupDepartments.FindAsync(
            gd => gd.QueryGroupId == request.GroupId, cancellationToken);
        foreach (var gd in existing)
            _unitOfWork.QueryGroupDepartments.Delete(gd);

        foreach (var department in request.Departments)
        {
            await _unitOfWork.QueryGroupDepartments.AddAsync(new QueryGroupDepartment
            {
                QueryGroupId = request.GroupId,
                Department = department
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
