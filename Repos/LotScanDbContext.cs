using Microsoft.EntityFrameworkCore;
using fn_lot_scanner.Models;

namespace fn_lot_scanner.Repos;

// Snake_case column mapping comes from .UseSnakeCaseNamingConvention() in Program.cs,
// matching db-lot-scanner/migrations/ exactly without hand-written column mappings.
public class LotScanDbContext(DbContextOptions<LotScanDbContext> options) : DbContext(options)
{
    public DbSet<LotScanSession> LotScanSessions => Set<LotScanSession>();
    public DbSet<ScannedItem> ScannedItems => Set<ScannedItem>();
    public DbSet<LotScanPhoto> LotScanPhotos => Set<LotScanPhoto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LotScanSession>(e =>
        {
            e.ToTable("lot_scan_sessions");
            e.HasKey(s => s.Id);
            e.HasMany(s => s.Items).WithOne(i => i.Session).HasForeignKey(i => i.SessionId);
            e.HasMany(s => s.Photos).WithOne(p => p.Session).HasForeignKey(p => p.SessionId);
        });

        modelBuilder.Entity<ScannedItem>(e =>
        {
            e.ToTable("scanned_items");
            e.HasKey(i => i.Id);
        });

        modelBuilder.Entity<LotScanPhoto>(e =>
        {
            e.ToTable("lot_scan_photos");
            e.HasKey(p => p.Id);
        });
    }
}
