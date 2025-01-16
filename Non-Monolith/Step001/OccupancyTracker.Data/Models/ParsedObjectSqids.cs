using Sqids;

namespace OccupancyTracker.Data.Models
{
    public class ParsedOrganizationSqids
    {
        private SqidsEncoder<long> _sqidsEncoder = new SqidsEncoder<long>();

        public ParsedOrganizationSqids(SqidsEncoder<long> sqidsEncoder)
        {
            _sqidsEncoder = sqidsEncoder;
        }

        public long? OrganizationId { get; set; }
        public long? LocationId { get; set; }
        public long? EntranceId { get; set; }
        public long? EntranceCounterId { get; set; }

        public string OrganizationSqid { get { return GenerateSqid((long)OrganizationId); } }
        public string LocationSqid { get { return GenerateSqid(OrganizationId, LocationId); } }
        public string EntranceSqid { get { return GenerateSqid(OrganizationId, LocationId, EntranceId); } }
        public string EntranceCounterSqid { get { return GenerateSqid(OrganizationId, LocationId, EntranceId, EntranceCounterId); } }

        private string GenerateSqid(params long?[] ids)
        {
            if (ids.Any(id => id == null))
            {
                return string.Empty;
            }
            long[] nonNullIds = ids.Select(id => (long)id).ToArray();
            return _sqidsEncoder.Encode(nonNullIds);
        }
    }


}
