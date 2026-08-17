using Microsoft.EntityFrameworkCore;
using TheWatch.Core.Entities;

namespace TheWatch.Infrastructure.Data;

/// <summary>
/// Entity Framework Core DbContext for The Watch platform.
/// </summary>
public class WatchDbContext : DbContext
{
    public WatchDbContext(DbContextOptions<WatchDbContext> options) : base(options)
    {
    }

    // User & Profile entities
    public DbSet<User> Users => Set<User>();
    public DbSet<ResponderProfile> ResponderProfiles => Set<ResponderProfile>();

    // Incident entities
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<ResponderAssignment> ResponderAssignments => Set<ResponderAssignment>();
    public DbSet<TriggerPhrase> TriggerPhrases => Set<TriggerPhrase>();
    public DbSet<IncidentTimeline> IncidentTimelines => Set<IncidentTimeline>();
    public DbSet<PostIncidentReview> PostIncidentReviews => Set<PostIncidentReview>();
    public DbSet<Disagreement> Disagreements => Set<Disagreement>();
    public DbSet<IncidentCheckinRecord> IncidentCheckinRecords => Set<IncidentCheckinRecord>();
    public DbSet<IncidentSafetyConfirmation> IncidentSafetyConfirmations => Set<IncidentSafetyConfirmation>();

    // Evidence entities
    public DbSet<Evidence> EvidenceRecords => Set<Evidence>();
    public DbSet<EvidenceChainOfCustody> EvidenceChainOfCustody => Set<EvidenceChainOfCustody>();
    public DbSet<SummonerPhoto> SummonerPhotos => Set<SummonerPhoto>();

    // Responder entities
    public DbSet<DesignatedResponderSchedule> DesignatedResponderSchedules => Set<DesignatedResponderSchedule>();

    // Device & Notification entities
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    // Legal & Consent entities
    public DbSet<LegalAgreement> LegalAgreements => Set<LegalAgreement>();
    public DbSet<UserAgreementConsent> UserAgreementConsents => Set<UserAgreementConsent>();
    public DbSet<ParentalConsentRecord> ParentalConsentRecords => Set<ParentalConsentRecord>();

    // Evacuation entities
    public DbSet<EvacuationResourceOffer> EvacuationResourceOffers => Set<EvacuationResourceOffer>();
    public DbSet<EvacuationRequest> EvacuationRequests => Set<EvacuationRequest>();
    public DbSet<EvacuationMatchProposal> EvacuationMatchProposals => Set<EvacuationMatchProposal>();
    public DbSet<ActiveEvacuation> ActiveEvacuations => Set<ActiveEvacuation>();
    public DbSet<EvacuationLocation> EvacuationLocations => Set<EvacuationLocation>();
    public DbSet<EvacuationMessage> EvacuationMessages => Set<EvacuationMessage>();

    // Shelter entities
    public DbSet<TemporaryShelter> TemporaryShelters => Set<TemporaryShelter>();
    public DbSet<ShelterCheckIn> ShelterCheckIns => Set<ShelterCheckIn>();

    // Disaster entities
    public DbSet<DisasterZone> DisasterZones => Set<DisasterZone>();
    public DbSet<SafetyCheckIn> SafetyCheckIns => Set<SafetyCheckIn>();
    public DbSet<FuelStation> FuelStations => Set<FuelStation>();

    // Safety Settings
    public DbSet<SafetySettings> SafetySettings => Set<SafetySettings>();

    // Auth entities
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<MfaEnrollment> MfaEnrollments => Set<MfaEnrollment>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<DuressPin> DuressPins => Set<DuressPin>();
    public DbSet<SessionToken> SessionTokens => Set<SessionToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<StepUpChallenge> StepUpChallenges => Set<StepUpChallenge>();

    // Training & Onboarding entities
    public DbSet<TrainingModule> TrainingModules => Set<TrainingModule>();
    public DbSet<ResponderTrainingCompletion> ResponderTrainingCompletions => Set<ResponderTrainingCompletion>();
    public DbSet<BackgroundCheckRecord> BackgroundCheckRecords => Set<BackgroundCheckRecord>();

    // Admin & Audit entities
    public DbSet<AdminActionAudit> AdminActionAudits => Set<AdminActionAudit>();

    // Real-time entities
    public DbSet<SignalRSubscription> SignalRSubscriptions => Set<SignalRSubscription>();

    // Detection entities
    public DbSet<DetectionSession> DetectionSessions => Set<DetectionSession>();

    // Location entities (for Cosmos DB - may not be in SQL)
    public DbSet<LocationRecord> LocationRecords => Set<LocationRecord>();

    // Video entities
    public DbSet<VideoStream> VideoStreams => Set<VideoStream>();
    public DbSet<VideoStreamNote> VideoStreamNotes => Set<VideoStreamNote>();

    // HQ Broadcast entities
    public DbSet<HqBroadcast> HqBroadcasts => Set<HqBroadcast>();
    public DbSet<BroadcastDeliveryConfirmation> BroadcastDeliveryConfirmations => Set<BroadcastDeliveryConfirmation>();

    // Incident Timeline Events (different from IncidentTimeline)
    public DbSet<IncidentTimelineEvent> IncidentTimelineEvents => Set<IncidentTimelineEvent>();

    // Evidence Chain of Custody Events
    public DbSet<EvidenceChainOfCustodyEvent> EvidenceChainOfCustodyEvents => Set<EvidenceChainOfCustodyEvent>();

    // Distributed System Pattern entities
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<SagaInstance> SagaInstances => Set<SagaInstance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User entity configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.AccountType).HasMaxLength(50);
            entity.Property(e => e.PiiState).HasMaxLength(50);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Phone);

            // Soft delete filter
            entity.HasQueryFilter(e => e.DeletedAt == null);
        });

        // ResponderProfile entity configuration
        modelBuilder.Entity<ResponderProfile>(entity =>
        {
            entity.HasKey(e => e.ResponderId);
            entity.Property(e => e.BackgroundCheckStatus).HasMaxLength(50);
            entity.Property(e => e.ReliabilityRating).HasMaxLength(50);
            entity.Property(e => e.CurrentStatus).HasMaxLength(50);

            // One-to-one relationship with User
            entity.HasOne(e => e.User)
                .WithOne(u => u.ResponderProfile)
                .HasForeignKey<ResponderProfile>(e => e.ResponderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Incident entity configuration
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(e => e.IncidentId);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.IncidentType).HasMaxLength(50);
            entity.Property(e => e.LocationGeohash).HasMaxLength(12);
            entity.HasIndex(e => e.LocationGeohash);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ReportedAt);

            // Relationship with Summoner
            entity.HasOne(e => e.Summoner)
                .WithMany()
                .HasForeignKey(e => e.SummonerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ResponderAssignment entity configuration
        modelBuilder.Entity<ResponderAssignment>(entity =>
        {
            entity.HasKey(e => e.AssignmentId);
            entity.Property(e => e.Role).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => new { e.IncidentId, e.ResponderId });
            entity.HasIndex(e => e.Status);

            // Relationship with Incident
            entity.HasOne(e => e.Incident)
                .WithMany(i => i.ResponderAssignments)
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relationship with Responder
            entity.HasOne(e => e.Responder)
                .WithMany()
                .HasForeignKey(e => e.ResponderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Evidence entity configuration
        modelBuilder.Entity<Evidence>(entity =>
        {
            entity.HasKey(e => e.EvidenceId);
            entity.Property(e => e.ResponderRole).HasMaxLength(50);
            entity.Property(e => e.EvidenceType).HasMaxLength(50);
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.Property(e => e.StorageLocation).HasMaxLength(1000);
            entity.Property(e => e.Sha256Hash).HasMaxLength(64);
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.LegalHold);

            // Relationship with Incident
            entity.HasOne(e => e.Incident)
                .WithMany(i => i.Evidence)
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relationship with Uploader
            entity.HasOne(e => e.UploadedByResponder)
                .WithMany()
                .HasForeignKey(e => e.UploadedByResponderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // TriggerPhrase entity configuration
        modelBuilder.Entity<TriggerPhrase>(entity =>
        {
            entity.HasKey(e => e.PhraseId);
            entity.Property(e => e.PhraseText).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ResponseType).HasMaxLength(50);
            entity.Property(e => e.FeedbackMode).HasMaxLength(50);
            entity.Property(e => e.Priority).HasMaxLength(50);
            entity.HasIndex(e => e.PhraseText);
            entity.HasIndex(e => new { e.UserId, e.IsActive });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // IncidentTimeline entity configuration
        modelBuilder.Entity<IncidentTimeline>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventType).HasMaxLength(100);
            entity.Property(e => e.Actor).HasMaxLength(50);
            entity.Property(e => e.ActorPiiState).HasMaxLength(50);
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ActorUser)
                .WithMany()
                .HasForeignKey(e => e.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // PostIncidentReview entity configuration
        modelBuilder.Entity<PostIncidentReview>(entity =>
        {
            entity.HasKey(e => e.ReviewId);
            entity.Property(e => e.ReviewerRole).HasMaxLength(50);
            entity.HasIndex(e => e.IncidentId);

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Reviewer)
                .WithMany()
                .HasForeignKey(e => e.ReviewerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Disagreement entity configuration
        modelBuilder.Entity<Disagreement>(entity =>
        {
            entity.HasKey(e => e.DisagreementId);
            entity.Property(e => e.DisagreementType).HasMaxLength(100);
            entity.Property(e => e.Severity).HasMaxLength(50);
            entity.Property(e => e.ResolutionStatus).HasMaxLength(50);
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.ResolutionStatus);

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FlaggedByResponder)
                .WithMany()
                .HasForeignKey(e => e.FlaggedByResponderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ResolvedByAdmin)
                .WithMany()
                .HasForeignKey(e => e.ResolvedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // IncidentCheckinRecord entity configuration
        modelBuilder.Entity<IncidentCheckinRecord>(entity =>
        {
            entity.HasKey(e => e.CheckinId);
            entity.Property(e => e.CheckinGeohash).HasMaxLength(12);
            entity.Property(e => e.InitialAssessment).HasMaxLength(50);
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.ResponderId);
            entity.HasIndex(e => e.CheckedInAt);

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Responder)
                .WithMany()
                .HasForeignKey(e => e.ResponderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // IncidentSafetyConfirmation entity configuration
        modelBuilder.Entity<IncidentSafetyConfirmation>(entity =>
        {
            entity.HasKey(e => e.ConfirmationId);
            entity.Property(e => e.UserRole).HasMaxLength(50);
            entity.Property(e => e.FollowUpType).HasMaxLength(50);
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ConfirmedAt);

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // EvidenceChainOfCustody entity configuration
        modelBuilder.Entity<EvidenceChainOfCustody>(entity =>
        {
            entity.HasKey(e => e.CustodyEventId);
            entity.Property(e => e.EventType).HasMaxLength(100);
            entity.Property(e => e.ActorRole).HasMaxLength(50);
            entity.HasIndex(e => e.EvidenceId);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.Evidence)
                .WithMany()
                .HasForeignKey(e => e.EvidenceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // DesignatedResponderSchedule entity configuration
        modelBuilder.Entity<DesignatedResponderSchedule>(entity =>
        {
            entity.HasKey(e => e.DesignationId);
            entity.Property(e => e.CommitmentType).HasMaxLength(50);
            entity.Property(e => e.LocationGeohash).HasMaxLength(12);
            entity.Property(e => e.TimeZone).HasMaxLength(100);
            entity.HasIndex(e => e.LocationGeohash);
            entity.HasIndex(e => new { e.ResponderId, e.IsActive });
            entity.HasIndex(e => new { e.EffectiveStartDate, e.EffectiveEndDate });

            entity.HasOne(e => e.Responder)
                .WithMany()
                .HasForeignKey(e => e.ResponderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure one-to-many relationship with ScheduleOverride
            entity.HasMany(e => e.Overrides)
                .WithOne(o => o.Schedule)
                .HasForeignKey(o => o.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ScheduleOverride entity configuration
        modelBuilder.Entity<ScheduleOverride>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ScheduleId, e.Date });
            entity.HasIndex(e => e.Date);
        });

        // SummonerPhoto entity configuration
        modelBuilder.Entity<SummonerPhoto>(entity =>
        {
            entity.HasKey(e => e.PhotoId);
            entity.Property(e => e.StorageLocation).HasMaxLength(1000);
            entity.HasIndex(e => e.IncidentId).IsUnique();
            entity.HasIndex(e => e.AutoDeleteAt);

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Summoner)
                .WithMany()
                .HasForeignKey(e => e.SummonerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // DeviceToken entity configuration
        modelBuilder.Entity<DeviceToken>(entity =>
        {
            entity.HasKey(e => e.TokenId);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Platform).HasMaxLength(20);
            entity.HasIndex(e => e.Token);
            entity.HasIndex(e => new { e.UserId, e.IsActive });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // LegalAgreement entity configuration
        modelBuilder.Entity<LegalAgreement>(entity =>
        {
            entity.HasKey(e => e.AgreementId);
            entity.Property(e => e.AgreementType).HasMaxLength(100);
            entity.Property(e => e.Version).HasMaxLength(50);
            entity.Property(e => e.ContentUrl).HasMaxLength(1000);
            entity.HasIndex(e => new { e.AgreementType, e.Version });
        });

        // UserAgreementConsent entity configuration
        modelBuilder.Entity<UserAgreementConsent>(entity =>
        {
            entity.HasKey(e => e.ConsentId);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.HasIndex(e => new { e.UserId, e.AgreementId });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Agreement)
                .WithMany(a => a.UserConsents)
                .HasForeignKey(e => e.AgreementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ParentalConsentRecord entity configuration
        modelBuilder.Entity<ParentalConsentRecord>(entity =>
        {
            entity.HasKey(e => e.ConsentId);
            entity.Property(e => e.ParentName).HasMaxLength(255);
            entity.Property(e => e.ParentEmail).HasMaxLength(255);
            entity.Property(e => e.VerificationMethod).HasMaxLength(50);
            entity.HasIndex(e => e.MinorUserId);

            entity.HasOne(e => e.MinorUser)
                .WithMany()
                .HasForeignKey(e => e.MinorUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EvacuationResourceOffer entity configuration
        modelBuilder.Entity<EvacuationResourceOffer>(entity =>
        {
            entity.HasKey(e => e.OfferId);
            entity.Property(e => e.ResourceType).HasMaxLength(50);
            entity.Property(e => e.LocationGeohash).HasMaxLength(12);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.LocationGeohash);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.Provider)
                .WithMany()
                .HasForeignKey(e => e.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EvacuationRequest entity configuration
        modelBuilder.Entity<EvacuationRequest>(entity =>
        {
            entity.HasKey(e => e.RequestId);
            entity.Property(e => e.CurrentLocationGeohash).HasMaxLength(12);
            entity.Property(e => e.Urgency).HasMaxLength(50);
            entity.Property(e => e.DisasterType).HasMaxLength(50);
            entity.Property(e => e.PreferredResourceType).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.CurrentLocationGeohash);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.Evacuee)
                .WithMany()
                .HasForeignKey(e => e.EvacueeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EvacuationMatchProposal entity configuration
        modelBuilder.Entity<EvacuationMatchProposal>(entity =>
        {
            entity.HasKey(e => e.MatchId);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.DeclineReason).HasMaxLength(500);
            entity.HasIndex(e => e.RequestId);
            entity.HasIndex(e => e.OfferId);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.Request)
                .WithMany()
                .HasForeignKey(e => e.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Offer)
                .WithMany()
                .HasForeignKey(e => e.OfferId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ActiveEvacuation entity configuration
        modelBuilder.Entity<ActiveEvacuation>(entity =>
        {
            entity.HasKey(e => e.EvacuationId);
            entity.Property(e => e.ResourceType).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.Request)
                .WithMany(r => r.ActiveEvacuations)
                .HasForeignKey(e => e.RequestId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Offer)
                .WithMany(o => o.ActiveEvacuations)
                .HasForeignKey(e => e.OfferId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Evacuee)
                .WithMany()
                .HasForeignKey(e => e.EvacueeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Provider)
                .WithMany()
                .HasForeignKey(e => e.ProviderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // EvacuationLocation entity configuration
        modelBuilder.Entity<EvacuationLocation>(entity =>
        {
            entity.HasKey(e => e.LocationId);
            entity.Property(e => e.UserRole).HasMaxLength(50);
            entity.HasIndex(e => e.EvacuationId);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.Evacuation)
                .WithMany(ev => ev.Locations)
                .HasForeignKey(e => e.EvacuationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // EvacuationMessage entity configuration
        modelBuilder.Entity<EvacuationMessage>(entity =>
        {
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.FromRole).HasMaxLength(50);
            entity.Property(e => e.Priority).HasMaxLength(50);
            entity.HasIndex(e => e.EvacuationId);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.Evacuation)
                .WithMany(ev => ev.Messages)
                .HasForeignKey(e => e.EvacuationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FromUser)
                .WithMany()
                .HasForeignKey(e => e.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // TemporaryShelter entity configuration
        modelBuilder.Entity<TemporaryShelter>(entity =>
        {
            entity.HasKey(e => e.ShelterId);
            entity.Property(e => e.ShelterType).HasMaxLength(50);
            entity.Property(e => e.LocationGeohash).HasMaxLength(12);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.LocationGeohash);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.Provider)
                .WithMany()
                .HasForeignKey(e => e.ProviderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ShelterCheckIn entity configuration
        modelBuilder.Entity<ShelterCheckIn>(entity =>
        {
            entity.HasKey(e => e.CheckInId);
            entity.HasIndex(e => e.ShelterId);
            entity.HasIndex(e => e.EvacueeId);

            entity.HasOne(e => e.Shelter)
                .WithMany(s => s.CheckIns)
                .HasForeignKey(e => e.ShelterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Evacuee)
                .WithMany()
                .HasForeignKey(e => e.EvacueeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // DisasterZone entity configuration
        modelBuilder.Entity<DisasterZone>(entity =>
        {
            entity.HasKey(e => e.ZoneId);
            entity.Property(e => e.DisasterType).HasMaxLength(50);
            entity.Property(e => e.Severity).HasMaxLength(50);
            entity.Property(e => e.CenterGeohash).HasMaxLength(12);
            entity.Property(e => e.EvacuationOrder).HasMaxLength(50);
            entity.Property(e => e.IssuedBy).HasMaxLength(255);
            entity.HasIndex(e => e.CenterGeohash);
            entity.HasIndex(e => e.DisasterType);
        });

        // SafetyCheckIn entity configuration
        modelBuilder.Entity<SafetyCheckIn>(entity =>
        {
            entity.HasKey(e => e.CheckInId);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // FuelStation entity configuration
        modelBuilder.Entity<FuelStation>(entity =>
        {
            entity.HasKey(e => e.StationId);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.LocationGeohash).HasMaxLength(12);
            entity.HasIndex(e => e.LocationGeohash);
        });

        // RefreshToken entity configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.TokenId);
            entity.Property(e => e.TokenHash).HasMaxLength(64);
            entity.Property(e => e.DeviceId).HasMaxLength(255);
            entity.Property(e => e.RevokedReason).HasMaxLength(100);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => new { e.UserId, e.RevokedAt });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MfaEnrollment entity configuration
        modelBuilder.Entity<MfaEnrollment>(entity =>
        {
            entity.HasKey(e => e.EnrollmentId);
            entity.Property(e => e.Method).HasMaxLength(50);
            entity.HasIndex(e => new { e.UserId, e.IsActive });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // LoginAttempt entity configuration
        modelBuilder.Entity<LoginAttempt>(entity =>
        {
            entity.HasKey(e => e.AttemptId);
            entity.Property(e => e.IdentifierHash).HasMaxLength(64);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.HasIndex(e => new { e.IdentifierHash, e.AttemptedAt });
            entity.HasIndex(e => e.IpAddress);
        });

        // DuressPin entity configuration
        modelBuilder.Entity<DuressPin>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.DuressPinHash).HasMaxLength(255);
            entity.Property(e => e.SafePinHash).HasMaxLength(255);

            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<DuressPin>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SessionToken entity configuration
        modelBuilder.Entity<SessionToken>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.TokenHash).HasMaxLength(64);
            entity.Property(e => e.DeviceId).HasMaxLength(255);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => new { e.UserId, e.RevokedAt });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PasswordResetToken entity configuration
        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.TokenId);
            entity.Property(e => e.TokenHash).HasMaxLength(64);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => new { e.UserId, e.ExpiresAt });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // StepUpChallenge entity configuration
        modelBuilder.Entity<StepUpChallenge>(entity =>
        {
            entity.HasKey(e => e.ChallengeId);
            entity.Property(e => e.Purpose).HasMaxLength(100);
            entity.Property(e => e.AuthMethodUsed).HasMaxLength(50);
            entity.Property(e => e.StepUpToken).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => new { e.UserId, e.IsCompleted });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AdminActionAudit entity configuration
        modelBuilder.Entity<AdminActionAudit>(entity =>
        {
            entity.HasKey(e => e.ActionId);
            entity.Property(e => e.ActionType).HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.HasIndex(e => e.ActionType);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.AdminUser)
                .WithMany()
                .HasForeignKey(e => e.AdminUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // SignalRSubscription entity configuration
        modelBuilder.Entity<SignalRSubscription>(entity =>
        {
            entity.HasKey(e => e.SubscriptionId);
            entity.Property(e => e.SubscriptionType).HasMaxLength(50);
            entity.Property(e => e.ConnectionId).HasMaxLength(255);
            entity.HasIndex(e => e.ConnectionId);
            entity.HasIndex(e => e.ExpiresAt);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // TrainingModule entity configuration
        modelBuilder.Entity<TrainingModule>(entity =>
        {
            entity.HasKey(e => e.ModuleId);
            entity.Property(e => e.ModuleName).HasMaxLength(255);
            entity.Property(e => e.Difficulty).HasMaxLength(50);
        });

        // ResponderTrainingCompletion entity configuration
        modelBuilder.Entity<ResponderTrainingCompletion>(entity =>
        {
            entity.HasKey(e => e.CompletionId);
            entity.HasIndex(e => new { e.ResponderId, e.ModuleId });

            entity.HasOne(e => e.Responder)
                .WithMany()
                .HasForeignKey(e => e.ResponderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Module)
                .WithMany()
                .HasForeignKey(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // BackgroundCheckRecord entity configuration
        modelBuilder.Entity<BackgroundCheckRecord>(entity =>
        {
            entity.HasKey(e => e.CheckId);
            entity.Property(e => e.CheckStatus).HasMaxLength(50);
            entity.Property(e => e.Provider).HasMaxLength(255);
            entity.HasIndex(e => e.ResponderId);

            entity.HasOne(e => e.Responder)
                .WithMany()
                .HasForeignKey(e => e.ResponderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DetectionSession entity configuration
        modelBuilder.Entity<DetectionSession>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.HasIndex(e => new { e.UserId, e.Status });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LocationRecord>(entity =>
        {
            entity.HasKey(e => e.LocationId);
            entity.Property(e => e.Geohash).IsRequired().HasMaxLength(12);
            entity.HasIndex(e => new { e.UserId, e.Timestamp });
            entity.HasIndex(e => e.Geohash);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.IncidentId);
        });

        // VideoStream entity configuration
        modelBuilder.Entity<VideoStream>(entity =>
        {
            entity.HasKey(e => e.StreamId);
            entity.Property(e => e.StreamStatus).HasMaxLength(50);
            entity.Property(e => e.StorageLocation).HasMaxLength(1000);
            entity.HasIndex(e => e.IncidentId);

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Responder)
                .WithMany()
                .HasForeignKey(e => e.ResponderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // VideoStreamNote entity configuration
        modelBuilder.Entity<VideoStreamNote>(entity =>
        {
            entity.HasKey(e => e.NoteId);
            entity.HasIndex(e => e.StreamId);
            entity.HasIndex(e => e.TimestampSeconds);

            entity.HasOne(e => e.Stream)
                .WithMany()
                .HasForeignKey(e => e.StreamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // HqBroadcast entity configuration
        modelBuilder.Entity<HqBroadcast>(entity =>
        {
            entity.HasKey(e => e.BroadcastId);
            entity.Property(e => e.Severity).HasMaxLength(50);
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.SentAt);

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SentByUser)
                .WithMany()
                .HasForeignKey(e => e.SentByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BroadcastDeliveryConfirmation>(entity =>
        {
            entity.HasKey(e => e.ConfirmationId);
            entity.Property(e => e.DeliveryStatus).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Channel).HasMaxLength(50);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(e => new { e.BroadcastId, e.ResponderId }).IsUnique();
            entity.HasIndex(e => e.DeliveryStatus);

            entity.HasOne(e => e.Broadcast)
                .WithMany(e => e.DeliveryConfirmations)
                .HasForeignKey(e => e.BroadcastId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Responder)
                .WithMany()
                .HasForeignKey(e => e.ResponderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // IncidentTimelineEvent entity configuration
        modelBuilder.Entity<IncidentTimelineEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventType).HasMaxLength(100);
            entity.Property(e => e.Actor).HasMaxLength(50);
            entity.Property(e => e.ActorPiiState).HasMaxLength(50);
            entity.HasIndex(e => e.IncidentId);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.Incident)
                .WithMany()
                .HasForeignKey(e => e.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ActorUser)
                .WithMany()
                .HasForeignKey(e => e.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // EvidenceChainOfCustodyEvent entity configuration
        modelBuilder.Entity<EvidenceChainOfCustodyEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventType).HasMaxLength(100);
            entity.Property(e => e.ActorRole).HasMaxLength(50);
            entity.HasIndex(e => e.EvidenceId);
            entity.HasIndex(e => e.Timestamp);

            entity.HasOne(e => e.Evidence)
                .WithMany()
                .HasForeignKey(e => e.EvidenceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // SafetySettings entity configuration
        modelBuilder.Entity<SafetySettings>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.FeedbackMode).HasMaxLength(50);
            entity.Property(e => e.DeceptiveDisguiseApp).HasMaxLength(50);

            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<SafetySettings>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
