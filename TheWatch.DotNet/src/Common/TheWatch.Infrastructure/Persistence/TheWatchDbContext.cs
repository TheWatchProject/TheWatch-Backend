using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheWatch.Domain.Entities;
using TheWatch.Domain.Drones;
using TheWatch.Domain.Medical;

namespace TheWatch.Infrastructure.Persistence;

/// <summary>
/// Primary Entity Framework Core database context for the platform.
/// </summary>
/// <remarks>
/// Supports PostgreSQL and SQL Server with spatial indices and optimistic concurrency.
/// </remarks>
public class TheWatchDbContext : DbContext
{
    /// <summary>
    /// Gets or sets the Incidents entity set.
    /// </summary>
    public DbSet<Incident> Incidents => Set<Incident>();

    /// <summary>
    /// Gets or sets the Responders entity set.
    /// </summary>
    public DbSet<Responder> Responders => Set<Responder>();

    /// <summary>
    /// Gets or sets the Autonomous Drones entity set.
    /// </summary>
    public DbSet<AutonomousDrone> Drones => Set<AutonomousDrone>();

    /// <summary>
    /// Gets or sets the Patient Triage Vitals entity set.
    /// </summary>
    public DbSet<PatientVitals> PatientVitals => Set<PatientVitals>();

    /// <summary>
    /// Initializes a new instance of <see cref="TheWatchDbContext"/>.
    /// </summary>
    /// <param name="options">DbContext configuration options.</param>
    public TheWatchDbContext(DbContextOptions<TheWatchDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Configures entity mappings, relationships, and indices using Fluent API.
    /// </summary>
    /// <param name="modelBuilder">The model builder instance.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Incident Entity Mapping
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Severity).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.Latitude, e.Longitude });
        });

        // Responder Entity Mapping
        modelBuilder.Entity<Responder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.Status);
        });

        // Drone Entity Mapping
        modelBuilder.Entity<AutonomousDrone>(entity =>
        {
            entity.HasKey(e => e.DroneId);
            entity.Property(e => e.Model).IsRequired().HasMaxLength(128);
            entity.Property(e => e.FlightMode).IsRequired().HasMaxLength(64);
            entity.Ignore(e => e.FlightPlan); // Stored in document store or JSON column
        });

        // Patient Vitals Mapping
        modelBuilder.Entity<PatientVitals>(entity =>
        {
            entity.HasKey(e => e.PatientId);
            entity.Property(e => e.TriageCategory).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.TriageCategory);
        });
    }
}
