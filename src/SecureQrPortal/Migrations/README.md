# EF Core migrations

`202608250001_InitialCreate` is the v1.0.0 baseline migration. It contains provider-specific DDL selected by EF Core `ActiveProvider`, so the same migration assembly can initialize SQLite or Microsoft SQL Server 2022. `ApplicationDbContextFactory` supports future design-time migrations.
