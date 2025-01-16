using OccupancyTracker.Data.Models;
using Sqids;

public interface ISqidsEncoderFactory
{
    /// <summary>
    /// Encodes the organization ID.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <returns>The encoded organization ID.</returns>
    string EncodeOrganizationId(long organizationId);

    /// <summary>
    /// Encodes the location ID.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="locationId">The location ID.</param>
    /// <returns>The encoded location ID.</returns>
    string EncodeLocationId(long organizationId, long locationId);

    /// <summary>
    /// Encodes the entrance ID.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="locationId">The location ID.</param>
    /// <param name="entranceId">The entrance ID.</param>
    /// <returns>The encoded entrance ID.</returns>
    string EncodeEntranceId(long organizationId, long locationId, long entranceId);

    /// <summary>
    /// Encodes the entrance counter ID.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="locationId">The location ID.</param>
    /// <param name="entranceId">The entrance ID.</param>
    /// <param name="entranceCounterId">The entrance counter ID.</param>
    /// <returns>The encoded entrance counter ID.</returns>
    string EncodeEntranceCounterId(long organizationId, long locationId, long entranceId, long entranceCounterId);

    /// <summary>
    /// Encodes the user information ID.
    /// </summary>
    /// <param name="userInformationId">The user information ID.</param>
    /// <returns>The encoded user information ID.</returns>
    string EncodeUserInformation(long userInformationId);

    /// <summary>
    /// Encodes the invalid security attempt ID.
    /// </summary>
    /// <param name="InvalidSecurityAttemptId">The invalid security attempt ID.</param>
    /// <returns>The encoded invalid security attempt ID.</returns>
    string EncodeInvalidSecurityAttempt(long InvalidSecurityAttemptId);

    /// <summary>
    /// Decodes the user information SQID.
    /// </summary>
    /// <param name="userInformationSqid">The user information SQID.</param>
    /// <returns>The decoded user information ID.</returns>
    long DecodeUserInformation(string userInformationSqid);

    /// <summary>
    /// Decodes the SQIDs.
    /// </summary>
    /// <param name="OrganizationSqid">The organization SQID.</param>
    /// <param name="LocationSqid">The location SQID.</param>
    /// <param name="EntranceSqid">The entrance SQID.</param>
    /// <param name="EntranceCounterSqid">The entrance counter SQID.</param>
    /// <returns>The parsed organization SQIDs.</returns>
    ParsedOrganizationSqids DecodeSqids(string? OrganizationSqid = null, string? LocationSqid = null, string? EntranceSqid = null, string? EntranceCounterSqid = null);

    /// <summary>
    /// Encodes the invitation ID.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="invitationId">The invitation ID.</param>
    /// <returns>The encoded invitation ID.</returns>
    string EncodeInvitationId(long organizationId, long invitationId);
}


