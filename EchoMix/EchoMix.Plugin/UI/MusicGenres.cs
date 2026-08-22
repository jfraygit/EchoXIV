using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoMix.Plugin.UI;

/// A DJ's own Genres tags are (and stay) plain free text - see DrawTagChipEditor, which accepts literally
/// anything so a DJ can tag their profile with in-jokes or vibe descriptions ("Nonsense Mostly", "Whatever
/// the Vibe is") if they want to.
public static class MusicGenres
{
    public static readonly string[] Known =
    {
        "Pop", "Synth Pop", "Dance Pop", "Electropop", "Indie Pop", "Art Pop", "Dream Pop",
        "Bubblegum Pop", "K-Pop", "J-Pop", "C-Pop", "City Pop", "Power Pop",

        "Hip Hop", "Rap", "Trap", "Boom Bap", "Conscious Hip Hop", "Old School Hip Hop",
        "Drill", "Mumble Rap", "Emo Rap", "Cloud Rap", "Phonk", "Drift Phonk",

        "R&B", "Contemporary R&B", "Neo Soul", "Soul", "Motown", "Funk", "G-Funk", "Gospel",
        "Disco", "Nu-Disco",

        "House", "Deep House", "Tech House", "Progressive House", "Future House",
        "Tropical House", "Electro House", "Bass House", "Acid House", "Afro House",
        "Amapiano",

        "Techno", "Minimal Techno", "Melodic Techno", "Trance", "Progressive Trance",
        "Psytrance", "Uplifting Trance", "Vocal Trance", "Hard Trance",

        "Dubstep", "Riddim", "Brostep", "Melodic Dubstep", "Drum and Bass", "Liquid DnB",
        "Jungle", "Breakbeat", "Big Beat", "Garage", "UK Garage", "Speed Garage",
        "Future Garage", "Bassline", "Glitch Hop", "Future Bass", "Moombahton",

        "Hardstyle", "Hardcore", "Gabber", "Happy Hardcore", "Rawstyle", "UK Hardcore",

        "Downtempo", "Trip Hop", "Chillstep", "Chillwave", "Chillout", "Lo-Fi",
        "Lo-Fi Hip Hop", "Study Beats", "Ambient", "Dark Ambient", "Drone", "New Age",
        "Meditation", "Nature Sounds", "ASMR",

        "Synthwave", "Vaporwave", "Retrowave", "Outrun", "Electro", "Electroclash",
        "Nightcore", "Hyperpop", "Glitch", "IDM", "Experimental", "Noise", "Industrial",
        "EBM",

        "Reggae", "Dub", "Ska", "Dancehall", "Reggaeton", "Soca", "Calypso", "Salsa",
        "Bachata", "Merengue", "Cumbia", "Bossa Nova", "Samba", "Latin", "Flamenco",

        "Afrobeat", "Afrobeats", "Afro House", "Highlife",

        "Rock", "Classic Rock", "Alternative Rock", "Indie Rock", "Punk Rock", "Pop Punk",
        "Hard Rock", "Soft Rock", "Progressive Rock", "Psychedelic Rock", "Garage Rock",
        "Post Rock", "Math Rock", "Surf Rock", "Grunge", "Emo", "Post-Hardcore", "Ska Punk",
        "Hardcore Punk",

        "Metal", "Heavy Metal", "Death Metal", "Black Metal", "Thrash Metal", "Power Metal",
        "Symphonic Metal", "Nu Metal", "Doom Metal", "Metalcore", "Djent", "Industrial Metal",

        "Jazz", "Smooth Jazz", "Swing", "Bebop", "Blues", "Delta Blues",

        "Classical", "Baroque", "Romantic", "Opera", "Chamber Music", "Orchestral",
        "Soundtrack", "Video Game Music", "VGM", "Chiptune", "8-Bit",

        "Folk", "Indie Folk", "Country", "Bluegrass", "Americana", "Singer-Songwriter",
        "Acoustic",

        "Alternative", "Indie", "World Music", "Anime", "Musical Theater", "Holiday",
        "Christmas", "Comedy", "Spoken Word",
    };

    private static readonly HashSet<string> normalized = new(Known.Select(Normalize));

    public static bool IsKnown(string genre) => normalized.Contains(Normalize(genre));

    /// Case-insensitive, and treats hyphens/underscores as spaces so "Hip-Hop", "Hip Hop", and "hip_hop" all
    /// match the same entry - just enough normalization to absorb how people actually type these, not a
    /// fuzzy/edit-distance match.
    private static string Normalize(string value)
    {
        var cleaned = value.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }
}
