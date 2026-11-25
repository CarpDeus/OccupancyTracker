using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Configuration.UserSecrets;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using Sqids;

namespace OccupancyTracker.Service
{
    public class ProfileService : IProfileService
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly SqidsEncoder<long> _sqids;
        
        public ProfileService(IDbContextFactory<OccupancyContext> contextFactory, ISqidsEncoderFactory sqidsEncoderFactory)
        {
            
            _contextFactory = contextFactory;
            _sqids = sqidsEncoderFactory.CreateEncoder("Occupancy:Sqids:ProfileAlphabet", 6);
        }

        public Profile Get(string appUserId) => _contextFactory.CreateDbContext().Profiles.FirstOrDefault(e => e.AppUserId == appUserId);


        public Profile GetProfile(string sqid) => _contextFactory.CreateDbContext().Profiles.FirstOrDefault(e => e.userInformationSqid == sqid);

        public Profile Save(Profile profile)
        {
            using (var _context = _contextFactory.CreateDbContext())
            {
                if (profile.ProfileId == 0)
                {
                    _context.Profiles.Add(profile);
                    _context.SaveChanges();
                    profile.userInformationSqid = _sqids.Encode(new long[] { profile.ProfileId });
                }
                else
                {
                    if (string.IsNullOrEmpty(profile.userInformationSqid))
                    {
                        profile.userInformationSqid = _sqids.Encode(new long[] { profile.ProfileId });
                    }
                    _context.Profiles.Update(profile);
                }

                _context.SaveChanges();
            }
            return profile;
        }

        public Dictionary<string, string> GetProfileOrgs(string sqid)
        {
            Dictionary<string, string> retVal = new Dictionary<string, string>();
            //Profile profile = GetProfile(sqid);
            //if(profile == null) return retVal;
            //using (var _context = _contextFactory.CreateDbContext())
            //{
            //    var orgUsers = _context.OrganizationUsers.Where(e => e.UserInformationId == profile.ProfileId).ToList();
            //    foreach (var orgUser in orgUsers)
            //    {
            //        var org = _context.Organizations.FirstOrDefault(e => e.OrganizationId == orgUser.OrganizationId);
            //        if (org != null)
            //        {
            //            retVal.Add(org.OrganizationSqid, org.OrganizationName);
            //        }
            //    }
                return retVal;
            
        }


    }
}
