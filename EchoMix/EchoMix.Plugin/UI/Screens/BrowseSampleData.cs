#if DEBUG
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Fake shows and DJs for looking at the Browse grid without needing a dozen people live.
internal static class BrowseSampleData
{
    private static PublicShowsSnapshotMessage? shows;
    private static DjProfilesSnapshotMessage? profiles;
    private static readonly Dictionary<string, DjProfileDetailDto> details = new(StringComparer.Ordinal);
    private static DjStatsSnapshotMessage? monthStats;
    private static DjStatsSnapshotMessage? allTimeStats;

    private readonly record struct ShowSeed(
        string Room,
        string Show,
        string Dj,
        bool Venue,
        bool Locked,
        int Listeners,
        int MinutesLive,
        string[] Genres,
        string VenueName,
        string World,
        string DataCenter,
        string Area,
        string Ward,
        string Plot,
        (int R, int G, int B) Tint);

    private static readonly ShowSeed[] Seeds =
    {
        new("AX4K2", "Moonlight Sessions", "Selene", true, false, 34, 92,
            new[] { "House", "Deep House" }, "The Blue Hour", "Balmung", "Crystal", "The Lavender Beds", "8", "22", (64, 92, 180)),
        new("7QW3Z", "Static Bloom", "Nyx", false, false, 12, 18,
            new[] { "Drum & Bass" }, "", "", "", "", "", "", (150, 48, 96)),
        new("MM91P", "Aetheric Drift", "Caelum", true, true, 58, 211,
            new[] { "Trance", "Progressive" }, "Starfall Lounge", "Gilgamesh", "Aether", "Shirogane", "14", "40", (38, 140, 148)),
        new("TT22B", "Basement Tapes", "Orrin", false, false, 5, 6,
            new[] { "Lo-fi", "Jazz" }, "", "", "", "", "", "", (120, 96, 56)),
        new("VN8LQ", "Crystal Reverb", "Ilma", true, false, 81, 47,
            new[] { "Techno" }, "Vespers", "Mateus", "Crystal", "Empyreum", "3", "11", (92, 60, 160)),
        new("K3DR7", "Night Market", "Ravi", true, true, 19, 133,
            new[] { "Hip Hop", "Funk" }, "The Night Market", "Jenova", "Aether", "Mist", "21", "7", (176, 86, 40)),
        new("ZB5HX", "Slow Orbit", "Teya", false, false, 3, 2,
            new[] { "Ambient" }, "", "", "", "", "", "", (56, 108, 92)),
        new("QF6VN", "Hearthfire", "Bram", true, false, 27, 64,
            new[] { "House", "Disco" }, "Hearthfire Hall", "Omega", "Chaos", "The Goblet", "6", "33", (160, 120, 48)),
    };

    /// Built once and reused - the grid asks every frame, and encoding eight JPEGs per frame would be a far
    /// more interesting performance problem than anything it is meant to be testing.
    public static PublicShowsSnapshotMessage Shows => shows ??= BuildShows();

    public static DjProfilesSnapshotMessage Profiles => profiles ??= BuildProfiles();

    /// The full detail for one sample DJ, so the drill-down is actually walkable while sampling.
    public static DjProfileDetailDto? Detail(string profileId)
    {
        if (FindSummary(profileId) is not { } summary)
            return null;

        if (!details.TryGetValue(profileId, out var detail))
        {
            detail = BuildDetail(summary);
            details[profileId] = detail;
        }

        detail.LikeCount = summary.LikeCount;
        detail.IsLikedByRequester = summary.IsLikedByRequester;
        detail.FollowerCount = summary.FollowerCount;
        detail.IsFollowedByRequester = summary.IsFollowedByRequester;

        return detail;
    }

    /// Flips a like or a follow on a fixture, in place.
    public static void ToggleLike(string profileId)
    {
        if (FindSummary(profileId) is not { } summary)
            return;

        summary.IsLikedByRequester = !summary.IsLikedByRequester;
        summary.LikeCount = Math.Max(0, summary.LikeCount + (summary.IsLikedByRequester ? 1 : -1));
    }

    public static void ToggleFollow(string profileId)
    {
        if (FindSummary(profileId) is not { } summary)
            return;

        summary.IsFollowedByRequester = !summary.IsFollowedByRequester;
        summary.FollowerCount = Math.Max(0, summary.FollowerCount + (summary.IsFollowedByRequester ? 1 : -1));
    }

    /// Invented stats for the invented DJs, so the Stats tab can be judged without waiting a month for real
    /// numbers.
    public static DjStatsSnapshotMessage Stats(bool monthOnly) => monthOnly
        ? monthStats ??= BuildStats(true)
        : allTimeStats ??= BuildStats(false);

    private static DjStatsSnapshotMessage BuildStats(bool monthOnly)
    {
        var scale = monthOnly ? 1 : 7;

        var profiles = Profiles.Profiles;
        var requesterId = profiles.Count > 2 ? profiles[2].Id : null;

        var boards = new List<DjStatBoardDto>
        {
            Board("ShowsPlayed", p => ((Seed(p, 3) % 40) + 1) * scale),
            Board("OnAirSeconds", p => ((Seed(p, 5) % 90) + 2) * 3600L * scale),
            Board("ListenerSeconds", p => ((Seed(p, 7) % 900) + 20) * 3600L * scale),
            Board("PeakListeners", p => p.ListenerCount > 0 ? p.ListenerCount : (Seed(p, 11) % 30) + 2),
            Board("DaysActive", p => Math.Min(((Seed(p, 13) % 24) + 1) * scale, 365)),
            Board("UniqueListeners", p => ((Seed(p, 17) % 600) + 10) * scale),
            Board("Likes", p => p.LikeCount),
            Board("Followers", p => p.FollowerCount),
        };

        var you = new DjStatTotalsDto
        {
            ShowsPlayed = 11 * scale,
            OnAirSeconds = (((14 * 60) + 35) * 60) * scale,
            ListenerSeconds = 96 * 3600 * scale,
            PeakListeners = monthOnly ? 23 : 41,
            DaysActive = Math.Min(8 * scale, 365),
            UniqueListeners = 147 * scale,
        };

        return new DjStatsSnapshotMessage
        {
            Boards = boards,
            You = you,
            IsMonth = monthOnly,
            MonthKey = monthOnly ? "2026-10" : null,
            QualifyingMinutes = 15,
        };

        static int Seed(DjProfileSummaryDto profile, int salt) => Math.Abs(profile.Id.GetHashCode() / salt);

        DjStatBoardDto Board(string key, Func<DjProfileSummaryDto, long> value)
        {
            var rows = new List<(DjProfileSummaryDto Profile, long Value)>();
            foreach (var profile in profiles)
                rows.Add((profile, value(profile)));

            rows.Sort((a, b) => b.Value.CompareTo(a.Value));

            var board = new DjStatBoardDto { Key = key };
            var rank = 0;

            foreach (var row in rows.Take(10))
            {
                board.Entries.Add(new DjStatEntryDto
                {
                    Rank = ++rank,
                    ProfileId = row.Profile.Id,
                    DjName = row.Profile.DjName,
                    AvatarBase64 = row.Profile.AvatarBase64,
                    FrameColorR = row.Profile.FrameColorR,
                    FrameColorG = row.Profile.FrameColorG,
                    FrameColorB = row.Profile.FrameColorB,
                    FrameStyle = row.Profile.FrameStyle,
                    NameColorR = row.Profile.NameColorR,
                    NameColorG = row.Profile.NameColorG,
                    NameColorB = row.Profile.NameColorB,
                    NameEffect = row.Profile.NameEffect,
                    Value = row.Value,
                    IsRequester = requesterId != null && row.Profile.Id == requesterId,
                });
            }

            return board;
        }
    }

    private static DjProfileSummaryDto? FindSummary(string profileId)
    {
        foreach (var summary in Profiles.Profiles)
        {
            if (string.Equals(summary.Id, profileId, StringComparison.Ordinal))
                return summary;
        }

        return null;
    }

    private static DjProfileDetailDto BuildDetail(DjProfileSummaryDto summary)
    {
        var seed = Math.Abs(summary.Id.GetHashCode());
        var tint = (
            R: (int)(summary.FrameColorR * 255f),
            G: (int)(summary.FrameColorG * 255f),
            B: (int)(summary.FrameColorB * 255f));

        var availability = new List<DjAvailabilityDayDto>(7);
        for (var i = 0; i < 7; i++)
        {
            var on = ((seed >> i) & 1) == 1;
            var start = (18 + (seed % 5) + i) % 24;

            availability.Add(new DjAvailabilityDayDto
            {
                IsAvailable = on,
                TimeNote = on
                    ? AvailabilitySlots.Compose(start, AvailabilitySlots.DefaultEndFor(start), "ET")
                    : null,
            });
        }

        var venues = new List<SavedVenueDto>();
        if (seed % 3 != 0)
        {
            venues.Add(new SavedVenueDto
            {
                Id = $"{summary.Id}-venue",
                Name = $"{summary.DjName}'s Place",
                DataCenter = "Crystal",
                World = "Balmung",
                HousingArea = "The Lavender Beds",
                Ward = ((seed % 24) + 1).ToString(),
                Plot = ((seed % 60) + 1).ToString(),
            });
        }

        var badges = new List<DjBadgeDto>();
        if (seed % 4 != 1)
        {
            badges.Add(new DjBadgeDto
            {
                Id = DjBadgeDto.FounderOneOh,
                AwardedAtUtc = new DateTime(2026, 1 + (seed % 9), 1 + (seed % 27), 0, 0, 0, DateTimeKind.Utc),
            });
        }

        return new DjProfileDetailDto
        {
            Id = summary.Id,
            DjName = summary.DjName,
            Badges = badges,
            Bio = summary.Bio,
            Genres = new List<string>(summary.Genres),
            SavedVenues = venues,
            Availability = availability,
            AvatarBase64 = summary.AvatarBase64,
            BannerBase64 = GradientJpeg(tint),
            FrameStyle = summary.FrameStyle,
            FrameColorR = summary.FrameColorR,
            FrameColorG = summary.FrameColorG,
            FrameColorB = summary.FrameColorB,
            NameEffect = summary.NameEffect,
            NameColorR = summary.NameColorR,
            NameColorG = summary.NameColorG,
            NameColorB = summary.NameColorB,
            LikeCount = summary.LikeCount,
            IsLikedByRequester = summary.IsLikedByRequester,
            FollowerCount = summary.FollowerCount,
            IsFollowedByRequester = summary.IsFollowedByRequester,
            IsLiveNow = summary.IsLiveNow,
            LiveRoomCode = summary.LiveRoomCode,

            IsOwnProfile = false,
            ShowLinkedCharacters = seed % 4 == 0,
            LinkedCharacterNames = seed % 4 == 0
                ? new List<string> { $"{summary.DjName} Alt" }
                : new List<string>(),
        };
    }

    private static PublicShowsSnapshotMessage BuildShows()
    {
        var now = DateTime.UtcNow;
        var list = new List<PublicShowEntryDto>(Seeds.Length);

        foreach (var seed in Seeds)
        {
            list.Add(new PublicShowEntryDto
            {
                RoomCode = seed.Room,
                ShowName = seed.Show,
                DjName = seed.Dj,
                LiveSinceUtc = now.AddMinutes(-seed.MinutesLive),
                ListenerCount = seed.Listeners,
                IsVenueShow = seed.Venue,
                VenueName = seed.Venue ? seed.VenueName : null,
                VenueDataCenter = seed.Venue ? seed.DataCenter : null,
                VenueWorld = seed.Venue ? seed.World : null,
                VenueHousingArea = seed.Venue ? seed.Area : null,
                VenueWard = seed.Venue ? seed.Ward : null,
                VenuePlot = seed.Venue ? seed.Plot : null,
                HasPassword = seed.Locked,
                Genres = new List<string>(seed.Genres),

                ImageBase64 = seed.Room == "ZB5HX" ? null : GradientJpeg(seed.Tint),
            });
        }

        return new PublicShowsSnapshotMessage { Shows = list };
    }

    /// One profile per show, plus a few who are not live - a DJ list where everybody is live is not a DJ
    /// list.
    private static readonly (string Frame, string Effect)[] Cosmetics =
    {
        ("Glow", "Pulse"),
        ("Chase", "Rainbow"),
        ("Sparkle", "Wave"),
        ("Gradient", "Gradient"),
        ("Pulse", "Glow"),
        ("Rainbow", "Shimmer"),
        ("Dashed", "Chase"),
        ("Spin", "Flicker"),
        ("Confetti", "Marquee"),
        ("Brackets", "Glitch"),
        ("Ticks", "Typewriter"),
        ("Double", "None"),
    };

    private readonly record struct ProfileSeed(string Dj, string Bio, string[] Genres, (int R, int G, int B) Tint);

    private static readonly ProfileSeed[] ExtraProfiles =
    {
        new("Corvid", "Mostly breakbeat. Occasionally something slower, if the room earns it.",
            new[] { "Breakbeat", "Jungle" }, (108, 70, 150)),
        new("Sora", "Sunday afternoon sets. Nothing over 110bpm, ever.",
            new[] { "Lo-fi", "Chillout" }, (70, 140, 120)),
        new("Pyre", "Loud. Unapologetically.",
            new[] { "Hardstyle", "Techno" }, (180, 60, 50)),
        new("Wren", "Vinyl rips and old soul. Requests welcome.",
            new[] { "Soul", "Disco" }, (150, 120, 60)),
    };

    private static DjProfilesSnapshotMessage BuildProfiles()
    {
        var list = new List<DjProfileSummaryDto>();
        var index = 0;

        foreach (var seed in Seeds)
        {
            var (frame, effect) = Cosmetics[index % Cosmetics.Length];
            list.Add(new DjProfileSummaryDto
            {
                Id = $"sample-{seed.Room}",
                DjName = seed.Dj,
                Bio = $"{string.Join(" / ", seed.Genres)} sets, mostly on {(seed.Venue ? seed.World : "whatever's open")}.",
                Genres = new List<string>(seed.Genres),
                AvatarBase64 = AvatarJpeg(seed.Tint, seed.Dj),
                FrameStyle = frame,
                FrameColorR = seed.Tint.R / 255f,
                FrameColorG = seed.Tint.G / 255f,
                FrameColorB = seed.Tint.B / 255f,
                NameEffect = effect,
                NameColorR = seed.Tint.R / 255f,
                NameColorG = seed.Tint.G / 255f,
                NameColorB = seed.Tint.B / 255f,
                LikeCount = (seed.Listeners * 7) + 13,
                FollowerCount = (seed.Listeners * 19) + 41,
                IsLikedByRequester = index % 3 == 0,
                IsFollowedByRequester = index % 4 == 0,

                IsLiveNow = true,
                LiveRoomCode = seed.Room,
                ListenerCount = seed.Listeners,
            });

            index++;
        }

        foreach (var extra in ExtraProfiles)
        {
            var (frame, effect) = Cosmetics[index % Cosmetics.Length];
            list.Add(new DjProfileSummaryDto
            {
                Id = $"sample-extra-{index}",
                DjName = extra.Dj,
                Bio = extra.Bio,
                Genres = new List<string>(extra.Genres),
                AvatarBase64 = AvatarJpeg(extra.Tint, extra.Dj),
                FrameStyle = frame,
                FrameColorR = extra.Tint.R / 255f,
                FrameColorG = extra.Tint.G / 255f,
                FrameColorB = extra.Tint.B / 255f,
                NameEffect = effect,
                NameColorR = extra.Tint.R / 255f,
                NameColorG = extra.Tint.G / 255f,
                NameColorB = extra.Tint.B / 255f,
                LikeCount = 60 - (index * 3),
                FollowerCount = 200 - (index * 11),
                IsLikedByRequester = index % 3 == 0,
                IsFollowedByRequester = index % 5 == 0,
                IsLiveNow = false,
            });

            index++;
        }

        return new DjProfilesSnapshotMessage { Profiles = list };
    }

    /// A square avatar: the tint, a soft highlight, and the DJ's initial.
    private static string AvatarJpeg((int R, int G, int B) tint, string name)
    {
        using var bitmap = new Bitmap(ShowImageProcessor.DjAvatarSize, ShowImageProcessor.DjAvatarSize);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var dark = Color.FromArgb(tint.R / 3, tint.G / 3, tint.B / 3);
            var light = Color.FromArgb(Math.Min(255, tint.R + 40), Math.Min(255, tint.G + 40), Math.Min(255, tint.B + 40));

            using (var brush = new LinearGradientBrush(bounds, dark, light, 55f))
                graphics.FillRectangle(brush, bounds);

            using (var blob = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
                graphics.FillEllipse(blob, -bitmap.Width * 0.25f, -bitmap.Height * 0.4f, bitmap.Width, bitmap.Height);

            var initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            using var font = new Font("Segoe UI", bitmap.Height * 0.46f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var ink = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            using var centred = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            graphics.DrawString(initial, font, ink, bounds, centred);
        }

        return EncodeJpeg(bitmap);
    }

    /// A diagonal two-tone gradient at the real card size, through the real JPEG encoder, so these weigh what
    /// an actual uploaded image weighs.
    private static string GradientJpeg((int R, int G, int B) tint)
    {
        using var bitmap = new Bitmap(ShowImageProcessor.TargetWidth, ShowImageProcessor.TargetHeight);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var dark = Color.FromArgb(Math.Max(0, tint.R / 4), Math.Max(0, tint.G / 4), Math.Max(0, tint.B / 4));
            var light = Color.FromArgb(tint.R, tint.G, tint.B);

            using (var brush = new LinearGradientBrush(bounds, dark, light, 35f))
                graphics.FillRectangle(brush, bounds);

            using (var blob = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            {
                graphics.FillEllipse(blob, bitmap.Width * 0.55f, -bitmap.Height * 0.35f, bitmap.Width * 0.7f, bitmap.Height * 1.1f);
                graphics.FillEllipse(blob, -bitmap.Width * 0.2f, bitmap.Height * 0.5f, bitmap.Width * 0.6f, bitmap.Height * 0.9f);
            }

        }

        return EncodeJpeg(bitmap);
    }

    private static string EncodeJpeg(Bitmap bitmap)
    {
        var encoder = GetJpegEncoder();
        using var stream = new MemoryStream();

        if (encoder != null)
        {
            using var parameters = new EncoderParameters(1);
            using var quality = new EncoderParameter(Encoder.Quality, 80L);
            parameters.Param[0] = quality;
            bitmap.Save(stream, encoder, parameters);
        }
        else
        {
            bitmap.Save(stream, ImageFormat.Jpeg);
        }

        return Convert.ToBase64String(stream.ToArray());
    }

    private static ImageCodecInfo? GetJpegEncoder()
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == ImageFormat.Jpeg.Guid)
                return codec;
        }

        return null;
    }
}
#endif
