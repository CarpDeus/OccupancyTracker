using OccupancyTracker.Models;

namespace OccupancyTracker.IService
{
    public interface IProfileService
    {
        public  Profile Get(string appUserId);
        public  Profile GetProfile(string sqid);
        public Dictionary<string, string> GetProfileOrgs(string sqid);
        public Profile Save(Profile userInformation);
    }
}
