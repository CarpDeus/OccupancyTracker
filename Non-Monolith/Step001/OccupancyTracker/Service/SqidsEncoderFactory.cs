using Enyim.Caching;
using OccupancyTracker.Data.Models;

//using OccupancyTracker.Components.Admin.Pages;
using Sqids;
using System.Text.Json;

/// <summary>
/// Factory class for creating and managing Sqids encoders.
/// </summary>
public class SqidsEncoderFactory : ISqidsEncoderFactory
{
    private readonly IConfiguration _configuration;
    private readonly IMemcachedClient _memcachedClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqidsEncoderFactory"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="memcachedClient">The memcached client.</param>
    public SqidsEncoderFactory(IConfiguration configuration, IMemcachedClient memcachedClient)
    {
        _configuration = configuration;
        _memcachedClient = memcachedClient;
    }

    /// <summary>
    /// Gets the Sqids alphabet based on the provided alphabet name.
    /// </summary>
    /// <param name="alphabetName">Name of the alphabet.</param>
    /// <returns>The Sqids alphabet.</returns>
    private string GetSqidsAlphabet(string alphabetName)
    {
        const string cacheKey = "SqidsAlphabets";
        var sqidAlphabets = _memcachedClient.Get<List<SqidAlphabet>>(cacheKey)
                            ?? _configuration.GetSection("Sqids").Get<List<SqidAlphabet>>();

        if (sqidAlphabets == null)
        {
            throw new Exception("Failed to retrieve SqidAlphabets from configuration.");
        }

        _memcachedClient.Add(cacheKey, sqidAlphabets, TimeSpan.FromHours(1));

        return sqidAlphabets.FirstOrDefault(x => x.AlphabetName == alphabetName)?.Alphabet
               ?? sqidAlphabets.First().Alphabet;
    }

    /// <summary>
    /// Creates a new Sqids encoder.
    /// </summary>
    /// <param name="alphabetName">Name of the alphabet.</param>
    /// <param name="minLength">The minimum length of the encoded string.</param>
    /// <returns>A new Sqids encoder.</returns>
    private SqidsEncoder<long> CreateEncoder(string alphabetName, int minLength)
    {
        return new SqidsEncoder<long>(new()
        {
            Alphabet = GetSqidsAlphabet(alphabetName),
            MinLength = minLength
        });
    }

    /// <inheritdoc/>
    public string EncodeOrganizationId(long organizationId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(organizationId);
    }

    /// <inheritdoc/>
    public string EncodeLocationId(long organizationId, long locationId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(new[] { organizationId, locationId });
    }

    /// <inheritdoc/>
    public string EncodeEntranceId(long organizationId, long locationId, long entranceId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(new[] { organizationId, locationId, entranceId });
    }

    /// <inheritdoc/>
    public string EncodeEntranceCounterId(long organizationId, long locationId, long entranceId, long entranceCounterId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(new[] { organizationId, locationId, entranceId, entranceCounterId });
    }

    /// <inheritdoc/>
    public string EncodeUserInformation(long userInformationId)
    {
        var encoder = CreateEncoder("UserInformation", 6);
        return encoder.Encode(userInformationId);
    }

    /// <inheritdoc/>
    public string EncodeInvalidSecurityAttempt(long invalidSecurityAttemptId)
    {
        var encoder = CreateEncoder("UserInformation", 6);
        return encoder.Encode(invalidSecurityAttemptId);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public string EncodeInvitationId(long organizationId, long invitationId)
    {
        var encoder = CreateEncoder("Default", 6);
        return encoder.Encode(new[] { organizationId, invitationId });
    }

    /// <inheritdoc/>
    public long DecodeUserInformation(string userInformationSqid)
    {
        var encoder = CreateEncoder("UserInformation", 6);
        return encoder.Decode(userInformationSqid).Single();
    }
}
