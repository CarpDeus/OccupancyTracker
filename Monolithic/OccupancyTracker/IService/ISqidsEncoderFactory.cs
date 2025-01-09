using OccupancyTracker.Models;
using Sqids;

public interface ISqidsEncoderFactory
{
  //  SqidsEncoder<long> CreateEncoder();
  //  SqidsEncoder<long> CreateEncoder(string alphabetName, int minLength);

    string EncodeOrganizationId(long organizationId);
    string EncodeLocationId(long organizationId, long locationId);
    string EncodeEntranceId(long organizationId, long locationId, long entranceId);
    string EncodeEntranceCounterId(long organizationId, long locationId, long entranceId, long entranceCounterId);
    string EncodeUserInformation(long userInformationId);
    string EncodeInvalidSecurityAttempt(long InvalidSecurityAttemptId);
    long DecodeUserInformation(string userInformationSqid);
    ParsedOrganizationSqids DecodeSqids(string? OrganizationSqid = null, string? LocationSqid = null, string? EntranceSqid = null, string? EntranceCounterSqid = null);
    string EncodeInvitationId(long organizationId, long invitationId);


}


