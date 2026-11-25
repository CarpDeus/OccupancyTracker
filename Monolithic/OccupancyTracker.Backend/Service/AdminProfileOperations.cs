using System.Data;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IService;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public sealed class AdminProfileOperations(
    IDbContextFactory<OccupancyContext> contexts,
    RequestAuthorizationService authorization,
    IOccAuthorizationService profiles)
{
    public async Task<FacilityResult<UserInformation>> ExecuteAsync(string sqid, UserInformation? edit = null)
    {
        await using var db = await contexts.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await authorization.GetCurrentUserAsync(db);
        if (actor?.IsSuperAdmin != true) return new(OrganizationAccess.Forbidden);
        var target = await db.UserInformation.SingleOrDefaultAsync(x => x.UserInformationSqid == sqid);
        if (target == null) return new(OrganizationAccess.NotFound);
        if (edit != null)
        {
            if (edit.ContactAddress == null || edit.ContactPhoneNumber == null) return new(OrganizationAccess.Invalid);
            target.FirstName = edit.FirstName;
            target.LastName = edit.LastName;
            target.ContactAddress = edit.ContactAddress;
            target.ContactPhoneNumber = edit.ContactPhoneNumber;
            target.HasCompletedRegistration = profiles.HasCompletedRegistration(target);
            target.ModifiedBy = actor.UserInformationId;
            target.ModifiedDate = DateTime.Now;
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return new(OrganizationAccess.Success, target);
    }
}
