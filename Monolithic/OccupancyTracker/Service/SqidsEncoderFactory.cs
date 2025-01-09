using Enyim.Caching;
//using OccupancyTracker.Components.Admin.Pages;
using OccupancyTracker.Models;
using Sqids;
using System.Text.Json;

public class SqidsEncoderFactory : ISqidsEncoderFactory
{
    private readonly IConfiguration _configuration;
    private readonly IMemcachedClient _memcachedClient;

    public SqidsEncoderFactory(IConfiguration configuration, IMemcachedClient memcachedClient)
    {
        _configuration = configuration;
        _memcachedClient = memcachedClient;
    }

    private string GetSqidsAlphabet(string alphabetName)
    {
        const string cacheKey = "SqidsAlphabets";
        var sqidAlphabets = _memcachedClient.Get<List<SqidAlphabet>>(cacheKey)
                            ?? _configuration.GetSection("Occupancy:Sqids").Get<List<SqidAlphabet>>();

        if (sqidAlphabets == null)
        {
            throw new Exception("Failed to retrieve SqidAlphabets from configuration.");
        }

        _memcachedClient.Add(cacheKey, sqidAlphabets, TimeSpan.FromHours(1));

        return sqidAlphabets.FirstOrDefault(x => x.AlphabetName == alphabetName)?.Alphabet
               ?? sqidAlphabets.First().Alphabet;
    }

    private SqidsEncoder<long> CreateEncoder(string alphabetName, int minLength)
    {
        return new SqidsEncoder<long>(new()
        {
            Alphabet = GetSqidsAlphabet(alphabetName),
            MinLength = minLength
        });
    }

    public string EncodeOrganizationId(long organizationId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(organizationId);
    }

    public string EncodeLocationId(long organizationId, long locationId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(new[] { organizationId, locationId });
    }

    public string EncodeEntranceId(long organizationId, long locationId, long entranceId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(new[] { organizationId, locationId, entranceId });
    }

    public string EncodeEntranceCounterId(long organizationId, long locationId, long entranceId, long entranceCounterId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(new[] { organizationId, locationId, entranceId, entranceCounterId });
    }

    public string EncodeUserInformation(long userInformationId)
    {
        var encoder = CreateEncoder("UserInformation", 6);
        return encoder.Encode(userInformationId);
    }

    public string EncodeInvalidSecurityAttempt(long invalidSecurityAttemptId)
    {
        var encoder = CreateEncoder("UserInformation", 6);
        return encoder.Encode(invalidSecurityAttemptId);
    }

    public ParsedOrganizationSqids DecodeSqids(string? organizationSqid = null, string? locationSqid = null, string? entranceSqid = null, string? entranceCounterSqid = null)
    {
        var encoder = CreateEncoder("Default", 6);
        var parsedSqids = new ParsedOrganizationSqids(encoder);
        bool haveParsed = false;

        if (!string.IsNullOrEmpty(entranceCounterSqid) && entranceCounterSqid.ToLowerInvariant() != "new")
        {
            var decodedLongs = encoder.Decode(entranceCounterSqid);
            if (decodedLongs.Count != 4) throw new Exception($"EntranceCounterSqid ({entranceCounterSqid}) is invalid");
            parsedSqids.OrganizationId = decodedLongs[0];
            parsedSqids.LocationId = decodedLongs[1];
            parsedSqids.EntranceId = decodedLongs[2];
            parsedSqids.EntranceCounterId = decodedLongs[3];
            haveParsed = true;
        }

        if (!string.IsNullOrEmpty(entranceSqid) && entranceSqid.ToLowerInvariant() != "new")
        {
            var decodedLongs = encoder.Decode(entranceSqid);
            if (decodedLongs.Count != 3) throw new Exception($"EntranceSqid ({entranceSqid}) is invalid");
            if (haveParsed && (parsedSqids.OrganizationId != decodedLongs[0] || parsedSqids.LocationId != decodedLongs[1]))
            {
                throw new Exception("Sqid Decoding Error");
            }
            parsedSqids.OrganizationId = decodedLongs[0];
            parsedSqids.LocationId = decodedLongs[1];
            parsedSqids.EntranceId = decodedLongs[2];
        }

        if (!string.IsNullOrEmpty(locationSqid) && locationSqid.ToLowerInvariant() != "new")
        {
            var decodedLongs = encoder.Decode(locationSqid);
            if (decodedLongs.Count != 2) throw new Exception($"LocationSqid ({locationSqid}) is invalid");
            if (haveParsed && (parsedSqids.OrganizationId != decodedLongs[0] || parsedSqids.LocationId != decodedLongs[1]))
            {
                throw new Exception("Sqid Decoding Error");
            }
            parsedSqids.OrganizationId = decodedLongs[0];
            parsedSqids.LocationId = decodedLongs[1];
        }

        if (!string.IsNullOrEmpty(organizationSqid) && organizationSqid.ToLowerInvariant() != "new")
        {
            var decodedLongs = encoder.Decode(organizationSqid);
            if (decodedLongs.Count != 1) throw new Exception($"OrganizationSqid ({organizationSqid}) is invalid");
            if (haveParsed && parsedSqids.OrganizationId != decodedLongs[0])
            {
                throw new Exception("Sqid Decoding Error");
            }
            parsedSqids.OrganizationId = decodedLongs[0];
        }

        return parsedSqids;
    }

    public string EncodeInvitationId(long organizationId, long invitationId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(new[] { organizationId, invitationId });
    }

    public long DecodeUserInformation(string userInformationSqid)
    {
        var encoder = CreateEncoder("UserInformation", 6);
        return encoder.Decode(userInformationSqid).Single();
    }
}
