using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TheWatch.Core.Entities;
using TheWatch.Core.Interfaces;
using TheWatch.Core.Services;
using TheWatch.Functions.Utilities;

namespace TheWatch.Functions;

/// <summary>
/// Azure Functions for complete evacuation API.
/// Implements all endpoints from evacuation-api.yaml.
/// </summary>
public class EvacuationFunctions
{
    private readonly ILogger<EvacuationFunctions> _logger;
    private readonly IEvacuationRepository _evacuationRepository;
    private readonly IEvacuationMatcherService _matcherService;
    private readonly IRouteCalculatorService _routeCalculator;
    private readonly IShelterCapacityManager _shelterCapacity;
    private readonly IDisasterZoneRepository _disasterZoneRepository;
    private readonly GeohashService _geohashService;

    public EvacuationFunctions(
        ILogger<EvacuationFunctions> logger,
        IEvacuationRepository evacuationRepository,
        IEvacuationMatcherService matcherService,
        IRouteCalculatorService routeCalculator,
        IShelterCapacityManager shelterCapacity,
        IDisasterZoneRepository disasterZoneRepository)
    {
        _logger = logger;
        _evacuationRepository = evacuationRepository;
        _matcherService = matcherService;
        _routeCalculator = routeCalculator;
        _shelterCapacity = shelterCapacity;
        _disasterZoneRepository = disasterZoneRepository;
        _geohashService = new GeohashService();
    }

    #region Request Endpoints

    /// <summary>
    /// POST /requests/evacuate - Create evacuation request
    /// </summary>
    [Function("CreateEvacuationRequest")]
    public async Task<HttpResponseData> CreateEvacuationRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "requests/evacuate")] HttpRequestData req)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                return await CreateUnauthorizedResponse(req, "Valid authentication required");
            }

            var body = await req.ReadAsStringAsync();
            var createRequest = JsonSerializer.Deserialize<CreateEvacuationRequestDto>(body ?? "{}");

            if (createRequest == null || createRequest.CurrentLocation == null)
            {
                return await CreateBadRequestResponse(req, "Valid location required");
            }

            var geohash = _geohashService.Encode(
                createRequest.CurrentLocation.Latitude,
                createRequest.CurrentLocation.Longitude, 9);

            var request = new EvacuationRequest
            {
                RequestId = Guid.NewGuid(),
                EvacueeId = userId.Value,
                CurrentLocationLat = createRequest.CurrentLocation.Latitude,
                CurrentLocationLng = createRequest.CurrentLocation.Longitude,
                CurrentLocationGeohash = geohash,
                DestinationPreferenceLat = createRequest.DestinationPreference?.Latitude,
                DestinationPreferenceLng = createRequest.DestinationPreference?.Longitude,
                PartySize = createRequest.PartySize,
                HasPets = createRequest.HasPets,
                PetTypes = createRequest.PetTypes != null ? JsonSerializer.Serialize(createRequest.PetTypes) : null,
                SpecialNeeds = createRequest.SpecialNeeds != null ? JsonSerializer.Serialize(createRequest.SpecialNeeds) : null,
                HasVehicle = createRequest.HasVehicle,
                VehicleDisabled = createRequest.VehicleDisabled,
                Urgency = createRequest.Urgency ?? "immediate",
                DisasterType = createRequest.DisasterType ?? "other",
                PreferredResourceType = createRequest.PreferredResourceType ?? "any",
                ContactPhone = createRequest.ContactPhone,
                Notes = createRequest.Notes,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            await _evacuationRepository.CreateRequestAsync(request, CancellationToken.None);

            _logger.LogInformation(
                "Created evacuation request {RequestId} for user {UserId}",
                request.RequestId, userId.Value);

            // Trigger matching asynchronously
            _ = Task.Run(async () =>
            {
                await _matcherService.FindMatchesForRequestAsync(request.RequestId);
            });

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(MapToDto(request));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating evacuation request");
            return await CreateErrorResponse(req, "Failed to create evacuation request");
        }
    }

    /// <summary>
    /// GET /requests/{requestId} - Get evacuation request
    /// </summary>
    [Function("GetEvacuationRequest")]
    public async Task<HttpResponseData> GetEvacuationRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "requests/{requestId}")] HttpRequestData req,
        string requestId)
    {
        try
        {
            if (!Guid.TryParse(requestId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid request ID");
            }

            var request = await _evacuationRepository.GetRequestByIdAsync(id, CancellationToken.None);
            if (request == null)
            {
                return await CreateNotFoundResponse(req, "Request not found");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToDto(request));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting evacuation request {RequestId}", requestId);
            return await CreateErrorResponse(req, "Failed to get evacuation request");
        }
    }

    /// <summary>
    /// PATCH /requests/{requestId} - Update evacuation request
    /// </summary>
    [Function("UpdateEvacuationRequest")]
    public async Task<HttpResponseData> UpdateEvacuationRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "requests/{requestId}")] HttpRequestData req,
        string requestId)
    {
        try
        {
            if (!Guid.TryParse(requestId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid request ID");
            }

            var request = await _evacuationRepository.GetRequestByIdAsync(id, CancellationToken.None);
            if (request == null)
            {
                return await CreateNotFoundResponse(req, "Request not found");
            }

            var body = await req.ReadAsStringAsync();
            var updateDto = JsonSerializer.Deserialize<UpdateEvacuationRequestDto>(body ?? "{}");

            if (updateDto != null)
            {
                if (updateDto.CurrentLocation != null)
                {
                    request.CurrentLocationLat = updateDto.CurrentLocation.Latitude;
                    request.CurrentLocationLng = updateDto.CurrentLocation.Longitude;
                    request.CurrentLocationGeohash = _geohashService.Encode(
                        updateDto.CurrentLocation.Latitude,
                        updateDto.CurrentLocation.Longitude, 9);
                }

                if (updateDto.Urgency != null)
                    request.Urgency = updateDto.Urgency;
                if (updateDto.PartySize.HasValue)
                    request.PartySize = updateDto.PartySize.Value;
                if (updateDto.Notes != null)
                    request.Notes = updateDto.Notes;

                request.UpdatedAt = DateTime.UtcNow;
            }

            await _evacuationRepository.UpdateRequestStatusAsync(id, request.Status, CancellationToken.None);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToDto(request));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating evacuation request {RequestId}", requestId);
            return await CreateErrorResponse(req, "Failed to update evacuation request");
        }
    }

    /// <summary>
    /// POST /requests/{requestId}/cancel - Cancel evacuation request
    /// </summary>
    [Function("CancelEvacuationRequest")]
    public async Task<HttpResponseData> CancelEvacuationRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "requests/{requestId}/cancel")] HttpRequestData req,
        string requestId)
    {
        try
        {
            if (!Guid.TryParse(requestId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid request ID");
            }

            await _evacuationRepository.UpdateRequestStatusAsync(id, "cancelled", CancellationToken.None);

            _logger.LogInformation("Cancelled evacuation request {RequestId}", requestId);

            var request = await _evacuationRepository.GetRequestByIdAsync(id, CancellationToken.None);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(request != null ? MapToDto(request) : new { requestId = id, status = "cancelled" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling evacuation request {RequestId}", requestId);
            return await CreateErrorResponse(req, "Failed to cancel evacuation request");
        }
    }

    #endregion

    #region Offer Endpoints

    /// <summary>
    /// POST /offers/resources - Create resource offer
    /// </summary>
    [Function("CreateResourceOffer")]
    public async Task<HttpResponseData> CreateResourceOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "offers/resources")] HttpRequestData req)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                return await CreateUnauthorizedResponse(req, "Valid authentication required");
            }

            var body = await req.ReadAsStringAsync();
            var createDto = JsonSerializer.Deserialize<CreateResourceOfferDto>(body ?? "{}");

            if (createDto == null || createDto.Location == null)
            {
                return await CreateBadRequestResponse(req, "Valid location and resource type required");
            }

            var geohash = _geohashService.Encode(
                createDto.Location.Latitude,
                createDto.Location.Longitude, 9);

            var offer = new EvacuationResourceOffer
            {
                OfferId = Guid.NewGuid(),
                ProviderId = userId.Value,
                ResourceType = createDto.ResourceType ?? "any",
                LocationLat = createDto.Location.Latitude,
                LocationLng = createDto.Location.Longitude,
                LocationGeohash = geohash,
                ServiceRadiusKm = createDto.ServiceRadiusKm,
                AvailableFrom = createDto.AvailableFrom,
                AvailableUntil = createDto.AvailableUntil,
                ContactPhone = createDto.ContactPhone,
                Notes = createDto.Notes,
                Status = "active",
                CreatedAt = DateTime.UtcNow
            };

            await _evacuationRepository.CreateOfferAsync(offer, CancellationToken.None);

            _logger.LogInformation(
                "Created resource offer {OfferId} for provider {ProviderId}",
                offer.OfferId, userId.Value);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(MapOfferToDto(offer));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating resource offer");
            return await CreateErrorResponse(req, "Failed to create resource offer");
        }
    }

    /// <summary>
    /// GET /offers/resources/{offerId} - Get resource offer
    /// </summary>
    [Function("GetResourceOffer")]
    public async Task<HttpResponseData> GetResourceOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offers/resources/{offerId}")] HttpRequestData req,
        string offerId)
    {
        try
        {
            if (!Guid.TryParse(offerId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid offer ID");
            }

            var offer = await _evacuationRepository.GetOfferByIdAsync(id, CancellationToken.None);
            if (offer == null)
            {
                return await CreateNotFoundResponse(req, "Offer not found");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapOfferToDto(offer));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting resource offer {OfferId}", offerId);
            return await CreateErrorResponse(req, "Failed to get resource offer");
        }
    }

    /// <summary>
    /// POST /offers/resources/{offerId}/withdraw - Withdraw resource offer
    /// </summary>
    [Function("WithdrawResourceOffer")]
    public async Task<HttpResponseData> WithdrawResourceOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "offers/resources/{offerId}/withdraw")] HttpRequestData req,
        string offerId)
    {
        try
        {
            if (!Guid.TryParse(offerId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid offer ID");
            }

            await _evacuationRepository.UpdateOfferStatusAsync(id, "withdrawn", CancellationToken.None);

            _logger.LogInformation("Withdrew resource offer {OfferId}", offerId);

            var offer = await _evacuationRepository.GetOfferByIdAsync(id, CancellationToken.None);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(offer != null ? MapOfferToDto(offer) : new { offerId = id, status = "withdrawn" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error withdrawing resource offer {OfferId}", offerId);
            return await CreateErrorResponse(req, "Failed to withdraw resource offer");
        }
    }

    #endregion

    #region Match Endpoints

    /// <summary>
    /// GET /matches - List match proposals
    /// </summary>
    [Function("ListMatches")]
    public async Task<HttpResponseData> ListMatches(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "matches")] HttpRequestData req)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var requestIdStr = query["requestId"];
            var offerIdStr = query["offerId"];

            IEnumerable<EvacuationMatchProposal> matches;

            if (!string.IsNullOrEmpty(requestIdStr) && Guid.TryParse(requestIdStr, out var requestId))
            {
                matches = await _evacuationRepository.GetMatchesForRequestAsync(requestId, CancellationToken.None);
            }
            else if (!string.IsNullOrEmpty(offerIdStr) && Guid.TryParse(offerIdStr, out var offerId))
            {
                matches = await _evacuationRepository.GetMatchesForOfferAsync(offerId, CancellationToken.None);
            }
            else
            {
                return await CreateBadRequestResponse(req, "Either requestId or offerId required");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                items = matches.Select(m => new
                {
                    matchId = m.MatchId,
                    requestId = m.RequestId,
                    offerId = m.OfferId,
                    score = m.Score,
                    status = m.Status,
                    createdAt = m.CreatedAt,
                    expiresAt = m.ExpiresAt
                }),
                nextCursor = (string?)null
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing matches");
            return await CreateErrorResponse(req, "Failed to list matches");
        }
    }

    /// <summary>
    /// POST /matches/{matchId}/respond - Respond to match proposal
    /// </summary>
    [Function("RespondToMatch")]
    public async Task<HttpResponseData> RespondToMatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "matches/{matchId}/respond")] HttpRequestData req,
        string matchId)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                return await CreateUnauthorizedResponse(req, "Valid authentication required");
            }

            if (!Guid.TryParse(matchId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid match ID");
            }

            var body = await req.ReadAsStringAsync();
            var responseDto = JsonSerializer.Deserialize<MatchResponseDto>(body ?? "{}");

            if (responseDto == null)
            {
                return await CreateBadRequestResponse(req, "Valid decision required");
            }

            object result;
            if (responseDto.Decision == "accept")
            {
                var evacuation = await _matcherService.AcceptMatchAsync(id, userId.Value, CancellationToken.None);
                result = new
                {
                    matchId = id,
                    decision = "accept",
                    evacuationId = evacuation.EvacuationId,
                    recordedAt = DateTime.UtcNow
                };

                _logger.LogInformation("Match {MatchId} accepted by provider {ProviderId}", id, userId.Value);
            }
            else
            {
                await _matcherService.DeclineMatchAsync(id, userId.Value, responseDto.Reason ?? "declined", CancellationToken.None);
                result = new
                {
                    matchId = id,
                    decision = "decline",
                    evacuationId = (Guid?)null,
                    recordedAt = DateTime.UtcNow
                };

                _logger.LogInformation("Match {MatchId} declined by provider {ProviderId}", id, userId.Value);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error responding to match {MatchId}", matchId);
            return await CreateErrorResponse(req, "Failed to respond to match");
        }
    }

    #endregion

    #region Evacuation Endpoints

    /// <summary>
    /// GET /evacuations/{evacuationId} - Get active evacuation
    /// </summary>
    [Function("GetEvacuation")]
    public async Task<HttpResponseData> GetEvacuation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "evacuations/{evacuationId}")] HttpRequestData req,
        string evacuationId)
    {
        try
        {
            if (!Guid.TryParse(evacuationId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid evacuation ID");
            }

            var evacuation = await _evacuationRepository.GetActiveEvacuationAsync(id, CancellationToken.None);
            if (evacuation == null)
            {
                return await CreateNotFoundResponse(req, "Evacuation not found");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                evacuationId = evacuation.EvacuationId,
                requestId = evacuation.RequestId,
                offerId = evacuation.OfferId,
                evacueeId = evacuation.EvacueeId,
                providerId = evacuation.ProviderId,
                resourceType = evacuation.ResourceType,
                status = evacuation.Status,
                createdAt = evacuation.CreatedAt,
                updatedAt = evacuation.UpdatedAt
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting evacuation {EvacuationId}", evacuationId);
            return await CreateErrorResponse(req, "Failed to get evacuation");
        }
    }

    /// <summary>
    /// POST /evacuations/{evacuationId}/location - Update evacuation location
    /// </summary>
    [Function("UpdateEvacuationLocation")]
    public async Task<HttpResponseData> UpdateEvacuationLocation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "evacuations/{evacuationId}/location")] HttpRequestData req,
        string evacuationId)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                return await CreateUnauthorizedResponse(req, "Valid authentication required");
            }

            if (!Guid.TryParse(evacuationId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid evacuation ID");
            }

            var body = await req.ReadAsStringAsync();
            var locationDto = JsonSerializer.Deserialize<UpdateLocationDto>(body ?? "{}");

            if (locationDto == null || locationDto.Location == null)
            {
                return await CreateBadRequestResponse(req, "Valid location required");
            }

            var location = new EvacuationLocation
            {
                LocationId = Guid.NewGuid(),
                EvacuationId = id,
                UserId = userId.Value,
                UserRole = locationDto.UserRole ?? "evacuee",
                LocationLat = locationDto.Location.Latitude,
                LocationLng = locationDto.Location.Longitude,
                Timestamp = DateTime.UtcNow
            };

            await _evacuationRepository.CreateLocationAsync(location, CancellationToken.None);

            _logger.LogInformation(
                "Updated location for evacuation {EvacuationId}, user {UserId}",
                evacuationId, userId.Value);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating evacuation location");
            return await CreateErrorResponse(req, "Failed to update location");
        }
    }

    /// <summary>
    /// POST /evacuations/{evacuationId}/messages - Send evacuation message
    /// </summary>
    [Function("SendEvacuationMessage")]
    public async Task<HttpResponseData> SendEvacuationMessage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "evacuations/{evacuationId}/messages")] HttpRequestData req,
        string evacuationId)
    {
        try
        {
            var userId = JwtUtilities.ExtractUserIdFromToken(req);
            if (!userId.HasValue)
            {
                return await CreateUnauthorizedResponse(req, "Valid authentication required");
            }

            if (!Guid.TryParse(evacuationId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid evacuation ID");
            }

            var body = await req.ReadAsStringAsync();
            var messageDto = JsonSerializer.Deserialize<CreateMessageDto>(body ?? "{}");

            if (messageDto == null || string.IsNullOrEmpty(messageDto.Message))
            {
                return await CreateBadRequestResponse(req, "Valid message required");
            }

            var message = new EvacuationMessage
            {
                MessageId = Guid.NewGuid(),
                EvacuationId = id,
                FromUserId = userId.Value,
                FromRole = messageDto.FromRole ?? "evacuee",
                Message = messageDto.Message,
                Priority = messageDto.Priority ?? "normal",
                Timestamp = DateTime.UtcNow
            };

            await _evacuationRepository.CreateMessageAsync(message, CancellationToken.None);

            _logger.LogInformation(
                "Message sent in evacuation {EvacuationId} by {UserId}",
                evacuationId, userId.Value);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                messageId = message.MessageId,
                fromUserId = message.FromUserId,
                fromRole = message.FromRole,
                message = message.Message,
                priority = message.Priority,
                timestamp = message.Timestamp
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending evacuation message");
            return await CreateErrorResponse(req, "Failed to send message");
        }
    }

    /// <summary>
    /// GET /evacuations/{evacuationId}/messages - List evacuation messages
    /// </summary>
    [Function("ListEvacuationMessages")]
    public async Task<HttpResponseData> ListEvacuationMessages(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "evacuations/{evacuationId}/messages")] HttpRequestData req,
        string evacuationId)
    {
        try
        {
            if (!Guid.TryParse(evacuationId, out var id))
            {
                return await CreateBadRequestResponse(req, "Invalid evacuation ID");
            }

            var messages = await _evacuationRepository.GetEvacuationMessagesAsync(id, 50, CancellationToken.None);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                items = messages.Select(m => new
                {
                    messageId = m.MessageId,
                    fromUserId = m.FromUserId,
                    fromRole = m.FromRole,
                    message = m.Message,
                    priority = m.Priority,
                    timestamp = m.Timestamp
                }),
                nextCursor = (string?)null
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing evacuation messages");
            return await CreateErrorResponse(req, "Failed to list messages");
        }
    }

    #endregion

    #region Route and Shelter Endpoints

    /// <summary>
    /// POST /routes/evacuation-route - Calculate safe evacuation route
    /// </summary>
    [Function("CalculateEvacuationRoute")]
    public async Task<HttpResponseData> CalculateEvacuationRoute(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "routes/evacuation-route")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadAsStringAsync();
            var routeRequest = JsonSerializer.Deserialize<CalculateRouteDto>(body ?? "{}");

            if (routeRequest == null || routeRequest.From == null)
            {
                return await CreateBadRequestResponse(req, "Valid starting location required");
            }

            var routeResponse = await _routeCalculator.CalculateRouteAsync(
                routeRequest.From.Latitude,
                routeRequest.From.Longitude,
                routeRequest.To?.Latitude,
                routeRequest.To?.Longitude,
                routeRequest.AvoidDisasterTypes ?? new List<string>(),
                routeRequest.IncludeFuelStops ?? true,
                routeRequest.IncludeShelters ?? true,
                CancellationToken.None);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(routeResponse);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating evacuation route");
            return await CreateErrorResponse(req, "Failed to calculate route");
        }
    }

    /// <summary>
    /// GET /shelters - List shelters near location
    /// </summary>
    [Function("ListShelters")]
    public async Task<HttpResponseData> ListShelters(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "shelters")] HttpRequestData req)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var latStr = query["latitude"];
            var lngStr = query["longitude"];
            var radiusStr = query["radiusKm"];

            if (!double.TryParse(latStr, out var lat) || !double.TryParse(lngStr, out var lng))
            {
                return await CreateBadRequestResponse(req, "Valid latitude and longitude required");
            }

            var radius = int.TryParse(radiusStr, out var r) ? r : 25;

            var shelters = await _shelterCapacity.FindAvailableSheltersAsync(
                lat, lng, radius, 1, null, CancellationToken.None);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(shelters.Select(s => new
            {
                shelterId = s.ShelterId.ToString(),
                name = s.Name,
                location = new
                {
                    latitude = s.LocationLat,
                    longitude = s.LocationLng,
                    geohash = s.LocationGeohash
                },
                capacity = new
                {
                    total = s.Capacity,
                    available = s.Capacity - s.CurrentOccupancy
                },
                contactPhone = s.ContactPhone
            }));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing shelters");
            return await CreateErrorResponse(req, "Failed to list shelters");
        }
    }

    /// <summary>
    /// GET /disaster-zones - List active disaster zones
    /// </summary>
    [Function("ListDisasterZones")]
    public async Task<HttpResponseData> ListDisasterZones(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "disaster-zones")] HttpRequestData req)
    {
        try
        {
            var zones = await _disasterZoneRepository.GetActiveZonesAsync(CancellationToken.None);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(zones.Select(z => new
            {
                zoneId = z.ZoneId,
                name = z.Name,
                disasterType = z.DisasterType,
                severity = z.Severity,
                geohashPrefixes = JsonSerializer.Deserialize<List<string>>(z.GeohashPrefixes),
                updatedAt = z.UpdatedAt
            }));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing disaster zones");
            return await CreateErrorResponse(req, "Failed to list disaster zones");
        }
    }

    #endregion

    #region Helper Methods

    private object MapToDto(EvacuationRequest request)
    {
        return new
        {
            requestId = request.RequestId,
            evacueeId = request.EvacueeId,
            currentLocation = new
            {
                latitude = request.CurrentLocationLat,
                longitude = request.CurrentLocationLng,
                geohash = request.CurrentLocationGeohash
            },
            partySize = request.PartySize,
            hasPets = request.HasPets,
            urgency = request.Urgency,
            preferredResourceType = request.PreferredResourceType,
            status = request.Status,
            createdAt = request.CreatedAt,
            updatedAt = request.UpdatedAt
        };
    }

    private object MapOfferToDto(EvacuationResourceOffer offer)
    {
        return new
        {
            offerId = offer.OfferId,
            providerId = offer.ProviderId,
            resourceType = offer.ResourceType,
            location = new
            {
                latitude = offer.LocationLat,
                longitude = offer.LocationLng,
                geohash = offer.LocationGeohash
            },
            serviceRadiusKm = offer.ServiceRadiusKm,
            status = offer.Status,
            createdAt = offer.CreatedAt,
            updatedAt = offer.UpdatedAt
        };
    }

    private async Task<HttpResponseData> CreateBadRequestResponse(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }

    private async Task<HttpResponseData> CreateUnauthorizedResponse(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.Unauthorized);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }

    private async Task<HttpResponseData> CreateNotFoundResponse(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.NotFound);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.InternalServerError);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }

    #endregion

    #region DTOs

    private class CreateEvacuationRequestDto
    {
        public LocationDto? CurrentLocation { get; set; }
        public LocationDto? DestinationPreference { get; set; }
        public int PartySize { get; set; } = 1;
        public bool HasPets { get; set; }
        public List<string>? PetTypes { get; set; }
        public List<string>? SpecialNeeds { get; set; }
        public bool HasVehicle { get; set; }
        public bool VehicleDisabled { get; set; }
        public string? Urgency { get; set; }
        public string? DisasterType { get; set; }
        public string? PreferredResourceType { get; set; }
        public string? ContactPhone { get; set; }
        public string? Notes { get; set; }
    }

    private class UpdateEvacuationRequestDto
    {
        public LocationDto? CurrentLocation { get; set; }
        public string? Urgency { get; set; }
        public int? PartySize { get; set; }
        public string? Notes { get; set; }
    }

    private class CreateResourceOfferDto
    {
        public LocationDto? Location { get; set; }
        public string? ResourceType { get; set; }
        public double ServiceRadiusKm { get; set; }
        public DateTime? AvailableFrom { get; set; }
        public DateTime? AvailableUntil { get; set; }
        public string? ContactPhone { get; set; }
        public string? Notes { get; set; }
    }

    private class LocationDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    private class MatchResponseDto
    {
        public string Decision { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    private class UpdateLocationDto
    {
        public LocationDto? Location { get; set; }
        public string? UserRole { get; set; }
    }

    private class CreateMessageDto
    {
        public string Message { get; set; } = string.Empty;
        public string? FromRole { get; set; }
        public string? Priority { get; set; }
    }

    private class CalculateRouteDto
    {
        public LocationDto? From { get; set; }
        public LocationDto? To { get; set; }
        public List<string>? AvoidDisasterTypes { get; set; }
        public bool? IncludeFuelStops { get; set; }
        public bool? IncludeShelters { get; set; }
    }

    #endregion
}
