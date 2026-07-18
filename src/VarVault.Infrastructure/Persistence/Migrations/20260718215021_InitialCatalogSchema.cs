using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Collection",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsRuleBased = table.Column<bool>(type: "INTEGER", nullable: false),
                    RuleJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collection", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Profile",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DirPath = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Repository",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MountPath = table.Column<string>(type: "TEXT", nullable: false),
                    VolumeSerial = table.Column<string>(type: "TEXT", nullable: true),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    PriorityInTier = table.Column<int>(type: "INTEGER", nullable: false),
                    ReadSpeedMBps = table.Column<double>(type: "REAL", nullable: true),
                    WriteSpeedMBps = table.Column<double>(type: "REAL", nullable: true),
                    BenchmarkedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CapacityBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    FreeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    MinFreeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsOnline = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReadOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repository", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Setting",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Setting", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Tag",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tag", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSave",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: true),
                    Mtime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScannedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSave", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoadingPreset",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ProfileId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsRuleBased = table.Column<bool>(type: "INTEGER", nullable: false),
                    RuleJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoadingPreset", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoadingPreset_Profile_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ActivationLink",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<long>(type: "INTEGER", nullable: false),
                    VarFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    LinkPath = table.Column<string>(type: "TEXT", nullable: false),
                    LinkKind = table.Column<int>(type: "INTEGER", nullable: false),
                    AliasedMissingRefKey = table.Column<string>(type: "TEXT", nullable: true),
                    LinkSubfolder = table.Column<string>(type: "TEXT", nullable: true),
                    LinkType = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedByPresetId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivationLink", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivationLink_LoadingPreset_RequestedByPresetId",
                        column: x => x.RequestedByPresetId,
                        principalTable: "LoadingPreset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActivationLink_Profile_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionMember",
                columns: table => new
                {
                    CollectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    PackageId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionMember", x => new { x.CollectionId, x.PackageId });
                    table.ForeignKey(
                        name: "FK_CollectionMember_Collection_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collection",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VarFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryPath = table.Column<string>(type: "TEXT", nullable: false),
                    IsPreset = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreviewThumbRef = table.Column<string>(type: "TEXT", nullable: true),
                    Gender = table.Column<int>(type: "INTEGER", nullable: true),
                    GenderConfidence = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentItem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentItemPref",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContentItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentItemPref", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentItemPref_ContentItem_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "ContentItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Dependency",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VarFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    DependsOnRefKey = table.Column<string>(type: "TEXT", nullable: false),
                    DependsOnRefRaw = table.Column<string>(type: "TEXT", nullable: false),
                    RefKind = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolvedPackageId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsVersionSubstituted = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMissing = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResolvedVia = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dependency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationJob",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VarFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceRepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetRepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    BytesCopied = table.Column<long>(type: "INTEGER", nullable: false),
                    TempPath = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationJob", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Package",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VarName = table.Column<string>(type: "TEXT", nullable: false),
                    IdentityKey = table.Column<string>(type: "TEXT", nullable: false),
                    Creator = table.Column<string>(type: "TEXT", nullable: false),
                    PackageName = table.Column<string>(type: "TEXT", nullable: false),
                    VersionToken = table.Column<string>(type: "TEXT", nullable: false),
                    VersionSort = table.Column<long>(type: "INTEGER", nullable: false),
                    MetaCreator = table.Column<string>(type: "TEXT", nullable: true),
                    MetaPackage = table.Column<string>(type: "TEXT", nullable: true),
                    MetaDivergent = table.Column<bool>(type: "INTEGER", nullable: false),
                    LicenseType = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ProgramVersion = table.Column<string>(type: "TEXT", nullable: true),
                    MetaDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastIndexedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReverseDependentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFoundational = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanonicalVarFileId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Package", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackageContentCount",
                columns: table => new
                {
                    PackageId = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageContentCount", x => new { x.PackageId, x.Type });
                    table.ForeignKey(
                        name: "FK_PackageContentCount_Package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackageListItem",
                columns: table => new
                {
                    PackageId = table.Column<long>(type: "INTEGER", nullable: false),
                    VarName = table.Column<string>(type: "TEXT", nullable: false),
                    Creator = table.Column<string>(type: "TEXT", nullable: false),
                    PackageName = table.Column<string>(type: "TEXT", nullable: false),
                    VersionToken = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryType = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    OnlineInstanceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalInstanceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSingleCopy = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActualTierMin = table.Column<int>(type: "INTEGER", nullable: true),
                    Class = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasMissingDeps = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreviewThumbRef = table.Column<string>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageListItem", x => x.PackageId);
                    table.ForeignKey(
                        name: "FK_PackageListItem_Package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackageTag",
                columns: table => new
                {
                    PackageId = table.Column<long>(type: "INTEGER", nullable: false),
                    TagId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageTag", x => new { x.PackageId, x.TagId });
                    table.ForeignKey(
                        name: "FK_PackageTag_Package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackageTag_Tag_TagId",
                        column: x => x.TagId,
                        principalTable: "Tag",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PresetMember",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PresetId = table.Column<long>(type: "INTEGER", nullable: false),
                    PackageRefKey = table.Column<string>(type: "TEXT", nullable: false),
                    PackageRefRaw = table.Column<string>(type: "TEXT", nullable: false),
                    ResolutionMode = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolvedPackageId = table.Column<long>(type: "INTEGER", nullable: true),
                    ResolvedVersion = table.Column<string>(type: "TEXT", nullable: true),
                    IsVersionSubstituted = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresetMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PresetMember_LoadingPreset_PresetId",
                        column: x => x.PresetId,
                        principalTable: "LoadingPreset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PresetMember_Package_ResolvedPackageId",
                        column: x => x.ResolvedPackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SaveDependency",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserSaveId = table.Column<long>(type: "INTEGER", nullable: false),
                    DependsOnRefKey = table.Column<string>(type: "TEXT", nullable: false),
                    DependsOnRefRaw = table.Column<string>(type: "TEXT", nullable: false),
                    ResolvedPackageId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsMissing = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaveDependency", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaveDependency_Package_ResolvedPackageId",
                        column: x => x.ResolvedPackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SaveDependency_UserSave_UserSaveId",
                        column: x => x.UserSaveId,
                        principalTable: "UserSave",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrashItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OriginalPath = table.Column<string>(type: "TEXT", nullable: false),
                    TrashPath = table.Column<string>(type: "TEXT", nullable: false),
                    PackageId = table.Column<long>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    TrashedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrashItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrashItem_Package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UsageEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackageId = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageEvent_Package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsageStat",
                columns: table => new
                {
                    PackageId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UseCountTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    Use30d = table.Column<int>(type: "INTEGER", nullable: false),
                    Use90d = table.Column<int>(type: "INTEGER", nullable: false),
                    CentralityScore = table.Column<double>(type: "REAL", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    Class = table.Column<int>(type: "INTEGER", nullable: false),
                    ScoreHistory = table.Column<string>(type: "TEXT", nullable: true),
                    LastFlipAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsPinnedHot = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsForcedCold = table.Column<bool>(type: "INTEGER", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageStat", x => x.PackageId);
                    table.ForeignKey(
                        name: "FK_UsageStat_Package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VarAlias",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MissingRefKey = table.Column<string>(type: "TEXT", nullable: false),
                    MissingRefRaw = table.Column<string>(type: "TEXT", nullable: false),
                    ResolvedVarName = table.Column<string>(type: "TEXT", nullable: true),
                    ResolvedPackageId = table.Column<long>(type: "INTEGER", nullable: true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    PresetId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VarAlias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VarAlias_LoadingPreset_PresetId",
                        column: x => x.PresetId,
                        principalTable: "LoadingPreset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VarAlias_Package_ResolvedPackageId",
                        column: x => x.ResolvedPackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VarFile",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackageId = table.Column<long>(type: "INTEGER", nullable: true),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FileMtime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QuickHash = table.Column<string>(type: "TEXT", nullable: true),
                    ContentSignature = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadSignature = table.Column<string>(type: "TEXT", nullable: true),
                    ContentSignatureNoPath = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: true),
                    EncodingHealth = table.Column<int>(type: "INTEGER", nullable: false),
                    DetectedCodepage = table.Column<string>(type: "TEXT", nullable: true),
                    BrokenEntryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IntegrityStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    FixedFromVarFileId = table.Column<long>(type: "INTEGER", nullable: true),
                    SupersededByVarFileId = table.Column<long>(type: "INTEGER", nullable: true),
                    QuarantineKind = table.Column<int>(type: "INTEGER", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VarFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VarFile_Package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VarFile_Repository_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repository",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VarFile_VarFile_FixedFromVarFileId",
                        column: x => x.FixedFromVarFileId,
                        principalTable: "VarFile",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VarFile_VarFile_SupersededByVarFileId",
                        column: x => x.SupersededByVarFileId,
                        principalTable: "VarFile",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivationLink_ProfileId",
                table: "ActivationLink",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivationLink_RequestedByPresetId",
                table: "ActivationLink",
                column: "RequestedByPresetId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivationLink_VarFileId",
                table: "ActivationLink",
                column: "VarFileId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionMember_PackageId",
                table: "CollectionMember",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItem_Type",
                table: "ContentItem",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItem_VarFileId",
                table: "ContentItem",
                column: "VarFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItemPref_ContentItemId",
                table: "ContentItemPref",
                column: "ContentItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dependency_DependsOnRefKey",
                table: "Dependency",
                column: "DependsOnRefKey");

            migrationBuilder.CreateIndex(
                name: "IX_Dependency_ResolvedPackageId",
                table: "Dependency",
                column: "ResolvedPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Dependency_VarFileId_DependsOnRefKey",
                table: "Dependency",
                columns: new[] { "VarFileId", "DependsOnRefKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoadingPreset_Name",
                table: "LoadingPreset",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoadingPreset_ProfileId",
                table: "LoadingPreset",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationJob_VarFileId",
                table: "MigrationJob",
                column: "VarFileId",
                unique: true,
                filter: "\"State\" NOT IN (6, 7, 8)");

            migrationBuilder.CreateIndex(
                name: "IX_Package_CanonicalVarFileId",
                table: "Package",
                column: "CanonicalVarFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Package_Creator_PackageName_VersionSort",
                table: "Package",
                columns: new[] { "Creator", "PackageName", "VersionSort" });

            migrationBuilder.CreateIndex(
                name: "IX_Package_IdentityKey",
                table: "Package",
                column: "IdentityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Package_IsFavorite",
                table: "Package",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_Package_VarName",
                table: "Package",
                column: "VarName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageListItem_Class_LastUsedAt_PackageId",
                table: "PackageListItem",
                columns: new[] { "Class", "LastUsedAt", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageListItem_Creator_TotalSize_PackageId",
                table: "PackageListItem",
                columns: new[] { "Creator", "TotalSize", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageListItem_HasMissingDeps",
                table: "PackageListItem",
                column: "HasMissingDeps");

            migrationBuilder.CreateIndex(
                name: "IX_PackageListItem_IsFavorite",
                table: "PackageListItem",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_PackageListItem_PrimaryType_VarName",
                table: "PackageListItem",
                columns: new[] { "PrimaryType", "VarName" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageTag_TagId",
                table: "PackageTag",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_PresetMember_PresetId",
                table: "PresetMember",
                column: "PresetId");

            migrationBuilder.CreateIndex(
                name: "IX_PresetMember_PresetId_PackageRefKey",
                table: "PresetMember",
                columns: new[] { "PresetId", "PackageRefKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PresetMember_ResolvedPackageId",
                table: "PresetMember",
                column: "ResolvedPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Profile_Name",
                table: "Profile",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repository_MountPath",
                table: "Repository",
                column: "MountPath");

            migrationBuilder.CreateIndex(
                name: "IX_Repository_Tier",
                table: "Repository",
                column: "Tier");

            migrationBuilder.CreateIndex(
                name: "IX_SaveDependency_DependsOnRefKey",
                table: "SaveDependency",
                column: "DependsOnRefKey");

            migrationBuilder.CreateIndex(
                name: "IX_SaveDependency_ResolvedPackageId",
                table: "SaveDependency",
                column: "ResolvedPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_SaveDependency_UserSaveId_DependsOnRefKey",
                table: "SaveDependency",
                columns: new[] { "UserSaveId", "DependsOnRefKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tag_NameKey",
                table: "Tag",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrashItem_PackageId",
                table: "TrashItem",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvent_PackageId_TimestampUnixMs",
                table: "UsageEvent",
                columns: new[] { "PackageId", "TimestampUnixMs" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageStat_Class",
                table: "UsageStat",
                column: "Class");

            migrationBuilder.CreateIndex(
                name: "IX_UserSave_Path",
                table: "UserSave",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VarAlias_MissingRefKey",
                table: "VarAlias",
                column: "MissingRefKey");

            migrationBuilder.CreateIndex(
                name: "IX_VarAlias_PresetId",
                table: "VarAlias",
                column: "PresetId");

            migrationBuilder.CreateIndex(
                name: "IX_VarAlias_ResolvedPackageId",
                table: "VarAlias",
                column: "ResolvedPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_ContentHash",
                table: "VarFile",
                column: "ContentHash",
                filter: "\"ContentHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_ContentSignature",
                table: "VarFile",
                column: "ContentSignature");

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_ContentSignatureNoPath",
                table: "VarFile",
                column: "ContentSignatureNoPath");

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_FixedFromVarFileId",
                table: "VarFile",
                column: "FixedFromVarFileId");

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_PackageId",
                table: "VarFile",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_PayloadSignature",
                table: "VarFile",
                column: "PayloadSignature");

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_RepositoryId_RelativePath",
                table: "VarFile",
                columns: new[] { "RepositoryId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VarFile_SupersededByVarFileId",
                table: "VarFile",
                column: "SupersededByVarFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_ActivationLink_VarFile_VarFileId",
                table: "ActivationLink",
                column: "VarFileId",
                principalTable: "VarFile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionMember_Package_PackageId",
                table: "CollectionMember",
                column: "PackageId",
                principalTable: "Package",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentItem_VarFile_VarFileId",
                table: "ContentItem",
                column: "VarFileId",
                principalTable: "VarFile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Dependency_Package_ResolvedPackageId",
                table: "Dependency",
                column: "ResolvedPackageId",
                principalTable: "Package",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Dependency_VarFile_VarFileId",
                table: "Dependency",
                column: "VarFileId",
                principalTable: "VarFile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MigrationJob_VarFile_VarFileId",
                table: "MigrationJob",
                column: "VarFileId",
                principalTable: "VarFile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Package_VarFile_CanonicalVarFileId",
                table: "Package",
                column: "CanonicalVarFileId",
                principalTable: "VarFile",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // FTS5 search over a curated per-package text blob. Tokenizer = trigram (Decisions-Log D1):
            // trigram does substring matching and works on space-less CJK (rowid = PackageId).
            // Not an EF entity — managed by the recompute pipeline, kept out of the model snapshot.
            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE \"PackageSearch\" USING fts5(\"Blob\", tokenize='trigram');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"PackageSearch\";");

            migrationBuilder.DropForeignKey(
                name: "FK_Package_VarFile_CanonicalVarFileId",
                table: "Package");

            migrationBuilder.DropTable(
                name: "ActivationLink");

            migrationBuilder.DropTable(
                name: "CollectionMember");

            migrationBuilder.DropTable(
                name: "ContentItemPref");

            migrationBuilder.DropTable(
                name: "Dependency");

            migrationBuilder.DropTable(
                name: "MigrationJob");

            migrationBuilder.DropTable(
                name: "PackageContentCount");

            migrationBuilder.DropTable(
                name: "PackageListItem");

            migrationBuilder.DropTable(
                name: "PackageTag");

            migrationBuilder.DropTable(
                name: "PresetMember");

            migrationBuilder.DropTable(
                name: "SaveDependency");

            migrationBuilder.DropTable(
                name: "Setting");

            migrationBuilder.DropTable(
                name: "TrashItem");

            migrationBuilder.DropTable(
                name: "UsageEvent");

            migrationBuilder.DropTable(
                name: "UsageStat");

            migrationBuilder.DropTable(
                name: "VarAlias");

            migrationBuilder.DropTable(
                name: "Collection");

            migrationBuilder.DropTable(
                name: "ContentItem");

            migrationBuilder.DropTable(
                name: "Tag");

            migrationBuilder.DropTable(
                name: "UserSave");

            migrationBuilder.DropTable(
                name: "LoadingPreset");

            migrationBuilder.DropTable(
                name: "Profile");

            migrationBuilder.DropTable(
                name: "VarFile");

            migrationBuilder.DropTable(
                name: "Package");

            migrationBuilder.DropTable(
                name: "Repository");
        }
    }
}
