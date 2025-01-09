using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.DTO;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using SendGrid.Helpers.Mail;
using Sqids;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OccupancyTracker.Service
{
    public class OrganizationUserService : IOrganizationUserService
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly IMemcachedClient _memcachedClient;
        private readonly IOccAuthorizationService _authorizationService;
        private readonly ISqidsEncoderFactory _organizationSqidsEncoderFactory;

        public OrganizationUserService(IDbContextFactory<OccupancyContext> contextFactory, ISqidsEncoderFactory organizationSqidsEncoderFactory, IMemcachedClient memcachedClient,
            IOccAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService;
            _organizationSqidsEncoderFactory = organizationSqidsEncoderFactory;
            _memcachedClient = memcachedClient;
            _contextFactory = contextFactory;
        }

        public async Task<List<OrganizationUser>> GetUserListForOrganizationAsync(string userInformationSqid, string ipAddress, string organizationSqid, bool forceCacheRefresh = false)
        {
            if (!await _authorizationService.HasAccessToOrganizationAsync(userInformationSqid, organizationSqid))
            {
                await _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, null, null, ipAddress, "User does not have access to organization", "You do not have access to this organization");
                return new List<OrganizationUser>();
            }

            string cacheKey = $"OrganizationUserList:{organizationSqid}";
            List<OrganizationUser> organizationUsers = null;

            

            //if (forceCacheRefresh || organizationUsers == null)
            {
                using (var context = _contextFactory.CreateDbContext())
                {
                    var organization = await context.Organizations.FirstOrDefaultAsync(x => x.OrganizationSqid == organizationSqid);
                    if (organization != null)
                    {
                        organizationUsers = await context.OrganizationUsers
                            .Where(x => x.OrganizationId == organization.OrganizationId)
                            .Include(e => e.UserInformation)
                            .ToListAsync();
                    }
                }

              
            }

            return organizationUsers ?? new List<OrganizationUser>();
        }

        public async Task<bool> InviteUserToOrganizationAsync(string userInformationSqid, string ipAddress, string organizationSqid, string email)
        {
            if (!await _authorizationService.IsOrgAdminAsync(userInformationSqid, organizationSqid))
            {
                await _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, null, null, ipAddress, "User does not have access to add users to this organization", "You do not have access to this organization");
                return false;
            }

            using (var context = _contextFactory.CreateDbContext())
            {
                var organization = await context.Organizations.FirstOrDefaultAsync(x => x.OrganizationSqid == organizationSqid);
                var userInformation = await context.UserInformation.FirstOrDefaultAsync(x => x.UserInformationSqid == userInformationSqid);

                if (organization == null || userInformation == null)
                {
                    return false;
                }

                var organizationInvitationCodes = new OrganizationInvitationCodes
                {
                    OrganizationId = organization.OrganizationId,
                    InvitationCode = string.Empty,
                    EmailAddress = email,
                    CreatedBy = userInformation.UserInformationSqid,
                    CreatedDate = DateTime.UtcNow
                };

                context.OrganizationInvitationCodes.Add(organizationInvitationCodes);
                await context.SaveChangesAsync();

                organizationInvitationCodes.InvitationCode = _organizationSqidsEncoderFactory.EncodeInvitationId(organization.OrganizationId, organizationInvitationCodes.OrganizationInvitationCodeId);
                context.OrganizationInvitationCodes.Update(organizationInvitationCodes);
                await context.SaveChangesAsync();

                var sendGridData = new SendGridData
                {
                    FromEmailAddress = "websupport@secure-scalable.solutions",
                    FromName = "WebSupport",
                    Subject = $"Invitation to join Occupancy Tracker for {organization.OrganizationName}",
                    HtmlContent = $"<p>You have been invited to join Occupancy Tracker for {organization.OrganizationName}. Please click <a href='https://occupancytracker.secure-scalable.solutions/profile/edit?invitationCode={organizationInvitationCodes.InvitationCode}'>here</a> to accept the invitation.</p>",
                    PlainTextContent = $"You have been invited to join Occupancy Tracker for {organization.OrganizationName}. Please open https://occupancytracker.secure-scalable.solutions and register/login and then enter the invitation code {organizationInvitationCodes.InvitationCode}.",
                    ToEmailAddress = email
                };

                var emailProcessorQueue = new EmailProcessorQueue
                {
                    EmailProcessorData = JsonSerializer.Serialize(sendGridData),
                    CreatedBy = userInformation.UserInformationId,
                    CreatedDate = DateTime.UtcNow,
                    OrganizationId = organization.OrganizationId
                };

                context.EmailProcessorQueue.Add(emailProcessorQueue);
                await context.SaveChangesAsync();

                return true;
            }
        }

        public async Task<bool> RedeemInvitationAsync(string userInformationSqid, string ipAddress, string invitationCode)
        {
            using (var context = _contextFactory.CreateDbContext())
            {
                var organizationInvitationCodes = await context.OrganizationInvitationCodes.FirstOrDefaultAsync(x => x.InvitationCode == invitationCode);
                if (organizationInvitationCodes == null)
                {
                    throw new Exception("Invalid invitation code");
                }

                var organization = await context.Organizations.FirstOrDefaultAsync(x => x.OrganizationId == organizationInvitationCodes.OrganizationId);
                var userInformation = await context.UserInformation.FirstOrDefaultAsync(x => x.UserInformationSqid == userInformationSqid);

                if (organization == null || userInformation == null)
                {
                    throw new Exception("Invalid invitation code");
                }
                // Add user to organization
                var organizationUser = new OrganizationUser
                {
                    OrganizationId = organization.OrganizationId,
                    UserInformationId = userInformation.UserInformationId,
                    CreatedBy = userInformationSqid,
                    CreatedDate = DateTime.UtcNow
                };
                context.OrganizationUsers.Add(organizationUser);
                await context.SaveChangesAsync();

                context.OrganizationUserRoles.Add(new OrganizationUserRole
                {
                    OrganizationUserId = organizationUser.OrganizationUsersId,
                    RoleName = AuthorizationRecords.Roles.User.Name,
                    OrganizationWide = true
                });
                await context.SaveChangesAsync();
                userInformation.BelongsToOrganization = true;
                context.UserInformation.Update(userInformation);
                context.Entry(userInformation).State = EntityState.Modified;

                if (await _authorizationService.HasAccessToOrganizationAsync(userInformationSqid, organization.OrganizationSqid))
                {
                    return true;
                }

                if (userInformation.EmailAddress.ToLowerInvariant() != organizationInvitationCodes.EmailAddress.ToLowerInvariant())
                {
                    throw new Exception("Invitation not for this user");
                }

                if (organizationInvitationCodes.InvitationRedeemed.HasValue)
                {
                    throw new Exception("Invitation already redeemed");
                }

                organizationInvitationCodes.InvitationRedeemed = DateOnly.FromDateTime(DateTime.UtcNow);
                context.OrganizationInvitationCodes.Update(organizationInvitationCodes);
                await context.SaveChangesAsync();
                return true;
            }
        }

        public async Task<List<OrganizationInvitationCodes>> GetInvitedUserListAsync(string userInformationSqid, string ipAddress, string organizationSqid)
        {
            if (!await _authorizationService.IsOrgAdminAsync(userInformationSqid, organizationSqid))
            {
                await _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, null, null, ipAddress, "User does not have access to add users to this organization", "You do not have access to this organization");
                return new List<OrganizationInvitationCodes>();
            }

            using (var context = _contextFactory.CreateDbContext())
            {
                var organization = await context.Organizations.FirstOrDefaultAsync(x => x.OrganizationSqid == organizationSqid);
                if (organization == null)
                {
                    return new List<OrganizationInvitationCodes>();
                }

                return await context.OrganizationInvitationCodes
                    .Where(x => x.OrganizationId == organization.OrganizationId && !x.InvitationRedeemed.HasValue)
                    .ToListAsync();
            }
        }

        public async Task<List<OrganizationUserRolesDto>> GetOrganizationUserRoles(string userInformationSqid, OrganizationUser organizationUser, string organizationSqid, string ipAddress, bool forceCacheRefresh = false)
        {
            if (!await _authorizationService.IsOrgAdminAsync(userInformationSqid, organizationSqid))
            {
                await _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, null, null, ipAddress, "User does not have access to add users to this organization", "You do not have access to this organization");
                return new List<OrganizationUserRolesDto>();
            }

            using (var context = _contextFactory.CreateDbContext())
            {
                var organization = await context.Organizations.FirstOrDefaultAsync(x => x.OrganizationSqid == organizationSqid);
                if (organization == null)
                {
                    return new List<OrganizationUserRolesDto>();
                }
                
                var userRoles = await context.OrganizationUserRoles
                    .Where(x => x.OrganizationUserId == organizationUser.OrganizationUsersId)
                    .ToListAsync();

                var locations = await context.Locations
                    .Where(x => x.OrganizationId == organization.OrganizationId && (x.CurrentStatus != Statuses.DataStatus.PermanentlyDeleted.Id))
                    .ToListAsync();

                var result = new List<OrganizationUserRolesDto>();

                foreach (var location in locations)
                {
                    var role = userRoles.FirstOrDefault(x => x.LocationId == location.LocationId);
                    if (role == null)
                    {
                        result.Add(new OrganizationUserRolesDto(location, organizationUser.OrganizationUsersId));
                    }
                    else
                    {
                        result.Add(new OrganizationUserRolesDto(location, role));
                    }
                }

                return result;
            }
        }

        Task<List<OrganizationUserRolesDto>> IOrganizationUserService.GetOrganizationUserRoles(string userInformationSqid, string organizationSqid, string ipAddress, bool forceCacheRefresh)
        {
            throw new NotImplementedException();
        }
    }
}