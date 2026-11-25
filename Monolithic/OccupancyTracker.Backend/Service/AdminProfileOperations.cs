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
            var address = edit.ContactAddress;
            if (edit.FirstName is null or { Length: > 256 } ||
                edit.LastName is null or { Length: > 256 } ||
                address.AddressLine1 is null or { Length: > 1024 } ||
                address.AddressLine2 is { Length: > 1024 } ||
                address.City is null or { Length: > 512 } ||
                address.State is null or { Length: > 512 } ||
                address.PostalCode is null or { Length: > 128 } ||
                address.Country is null or { Length: > 256 })
                return new(OrganizationAccess.Invalid);
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
