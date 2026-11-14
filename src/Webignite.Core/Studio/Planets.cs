namespace Webignite.Core.Studio;

/// <summary>The kinds of world a client can be on the Studio map.</summary>
public enum PlanetKind { Gas, Ocean, Rock, Ice, Desert }

/// <summary>
/// The surface of a planet as a flat map of the whole globe (as an atlas shows the Earth), worked out from a
/// number so the same client is always the same world. Colours are packed 0xRRGGBB. The map joins up left to
/// right, so a globe wrapped in it has no seam.
/// </summary>
public static class PlanetSurface
{
    public static PlanetKind KindOf(int seed) => (PlanetKind)((uint)seed % 5);

    /// <summary>
    /// The map, <paramref name="width"/> by <paramref name="height"/>, row by row from the north pole down.
    /// <paramref name="main"/> is the world's own colour, <paramref name="light"/> and <paramref name="dark"/> what
    /// it pales and deepens towards, and <paramref name="second"/> a colour for land on a world with seas.
    /// </summary>
    public static uint[] Make(PlanetKind kind, int seed, int width, int height, uint main, uint light, uint dark, uint second)
    {
        var map = new uint[width * height];
        var shift = (seed & 0xFFFF) * 0.37;
        var craters = kind == PlanetKind.Rock ? Craters(seed, 46) : [];
        // One storm on a gas world, at a place of its own.
        var stormLat = 0.25 + (uint)seed % 7 * 0.05;
        var stormLon = (uint)seed % 11 * 0.5;
        for (var row = 0; row < height; row++)
        {
            var lat = (0.5 - (row + 0.5) / height) * Math.PI;
            var (sinLat, cosLat) = Math.SinCos(lat);
            for (var column = 0; column < width; column++)
            {
                var lon = (column + 0.5) / width * Math.Tau;
                var (sinLon, cosLon) = Math.SinCos(lon);
                // The point on the globe; noise read here has no seam and doesn't pinch at the poles.
                var (x, y, z) = (cosLat * cosLon, sinLat, cosLat * sinLon);
                map[row * width + column] = kind switch
                {
                    PlanetKind.Gas => Gas(x, y, z, lat, lon, shift, stormLat, stormLon, main, light, dark),
                    PlanetKind.Ocean => Ocean(x, y, z, lat, shift, main, light, dark, second),
                    PlanetKind.Rock => Rock(x, y, z, shift, craters, main, light, dark),
                    PlanetKind.Ice => Ice(x, y, z, lat, shift, main, light, dark),
                    _ => Desert(x, y, z, lat, shift, main, light, dark),
                };
            }
        }
        return map;
    }

    // Bands running round the world, pushed about by turbulence, with fine streaks along them and one long storm.
    private static uint Gas(double x, double y, double z, double lat, double lon, double shift, double stormLat, double stormLon, uint main, uint light, uint dark)
    {
        var swirl = Fbm(x * 1.6 + shift, y * 1.6, z * 1.6, 4) - 0.5;
        var band = Math.Sin(lat * 9 + swirl * 3.2);
        var streak = Fbm(x * 2.2 + shift, y * 14, z * 2.2, 3) - 0.5;
        var tone = Math.Clamp(0.5 + 0.42 * band + 0.5 * streak, 0, 1);
        var colour = tone < 0.5 ? Mix(Mix(main, dark, 0.55), main, tone * 2) : Mix(main, Mix(main, light, 0.7), (tone - 0.5) * 2);
        // The storm: a pale oval with a deeper edge, a little south of the middle.
        var dLon = Math.Abs(Math.IEEERemainder(lon - stormLon, Math.Tau));
        var storm = Math.Sqrt(Math.Pow(dLon / 0.34, 2) + Math.Pow((lat + stormLat) / 0.13, 2));
        if (storm < 1)
            colour = Mix(colour, storm > 0.7 ? Mix(main, dark, 0.5) : Mix(main, light, 0.8), storm > 0.7 ? (1 - storm) / 0.3 * 0.8 : 0.85);
        return colour;
    }

    // Seas that deepen away from the shore, land that rises to pale heights, ice at the poles, and cloud over it all.
    private static uint Ocean(double x, double y, double z, double lat, double shift, uint main, uint light, uint dark, uint second)
    {
        var height = Fbm(x * 1.7 + shift, y * 1.7 + shift * 0.3, z * 1.7, 6);
        const double shore = 0.53;
        uint colour;
        if (height < shore)
            colour = Mix(Mix(main, dark, 0.72), Mix(main, light, 0.12), Math.Pow(height / shore, 3));
        else
            colour = Mix(Mix(second, dark, 0.25), Mix(second, light, 0.55), Math.Clamp((height - shore) * 5, 0, 1));
        var cap = Math.Abs(lat) - (1.12 + (Fbm(x * 4 + shift, y * 4, z * 4, 3) - 0.5) * 0.5);
        if (cap > 0)
            colour = Mix(colour, light, Math.Clamp(cap * 9, 0, 0.95));
        var cloud = Fbm(x * 2.6 - shift, y * 4.2, z * 2.6 + shift, 5);
        if (cloud > 0.55)
            colour = Mix(colour, light, Math.Clamp((cloud - 0.55) * 5.5, 0, 0.9));
        return colour;
    }

    // Grey highlands and darker plains, pocked with craters: a darker floor and a brighter rim.
    private static uint Rock(double x, double y, double z, double shift, (double X, double Y, double Z, double Size)[] craters, uint main, uint light, uint dark)
    {
        var plains = Fbm(x * 1.5 + shift, y * 1.5, z * 1.5, 4);
        var grain = Fbm(x * 9 + shift, y * 9, z * 9, 3);
        var tone = Math.Clamp(0.2 + plains * 0.75 + (grain - 0.5) * 0.3, 0, 1);
        var colour = Mix(Mix(main, dark, 0.6), Mix(main, light, 0.35), tone);
        foreach (var crater in craters)
        {
            var across = Math.Acos(Math.Clamp(x * crater.X + y * crater.Y + z * crater.Z, -1, 1)) / crater.Size;
            if (across < 0.82)
                colour = Mix(colour, dark, 0.38 * (1 - across * 0.5));
            else if (across < 1.08)
                colour = Mix(colour, light, 0.42 * (1 - Math.Abs(across - 0.95) / 0.13));
        }
        return colour;
    }

    // A smooth, pale world with faint bands and a soft mottling.
    private static uint Ice(double x, double y, double z, double lat, double shift, uint main, uint light, uint dark)
    {
        var drift = Fbm(x * 1.3 + shift, y * 1.3, z * 1.3, 4) - 0.5;
        var tone = Math.Clamp(0.5 + 0.22 * Math.Sin(lat * 6 + drift * 2.5) + drift * 0.35, 0, 1);
        return Mix(Mix(main, dark, 0.3), Mix(main, light, 0.6), tone);
    }

    // Dusty plains with darker country, fine detail, and pale caps at the poles.
    private static uint Desert(double x, double y, double z, double lat, double shift, uint main, uint light, uint dark)
    {
        var country = Fbm(x * 1.9 + shift, y * 1.9, z * 1.9, 5);
        var detail = Fbm(x * 8 + shift, y * 8, z * 8, 3) - 0.5;
        var tone = Math.Clamp(0.15 + country * 0.9 + detail * 0.25, 0, 1);
        var colour = Mix(Mix(main, dark, 0.62), Mix(main, light, 0.3), tone);
        // A long dark scar across it, as a canyon system.
        var scar = Math.Abs(lat + 0.15 + (Fbm(x * 3 + shift, y * 3, z * 3, 2) - 0.5) * 0.5);
        if (scar < 0.035 && country > 0.45)
            colour = Mix(colour, dark, 0.45);
        var cap = Math.Abs(lat) - (1.25 + detail * 0.2);
        return cap > 0 ? Mix(colour, light, Math.Clamp(cap * 10, 0, 0.9)) : colour;
    }

    private static (double X, double Y, double Z, double Size)[] Craters(int seed, int count)
    {
        var dice = (uint)seed * 2654435761u + 7u;
        double Roll()
        {
            dice = dice * 1664525u + 1013904223u;
            return (dice >> 8) % 100000 / 100000.0;
        }
        var craters = new (double, double, double, double)[count];
        for (var i = 0; i < count; i++)
        {
            // Spread evenly over the globe, not bunched at the poles.
            var up = Roll() * 2 - 1;
            var round = Roll() * Math.Tau;
            var flat = Math.Sqrt(1 - up * up);
            // Mostly small, a few large.
            var size = 0.035 + Math.Pow(Roll(), 3) * 0.2;
            craters[i] = (flat * Math.Cos(round), up, flat * Math.Sin(round), size);
        }
        return craters;
    }

    /// <summary>Two colours mixed: <paramref name="share"/> 0 is all the first, 1 all the second.</summary>
    public static uint Mix(uint a, uint b, double share)
    {
        share = Math.Clamp(share, 0, 1);
        var r = (int)((a >> 16 & 255) + ((int)(b >> 16 & 255) - (int)(a >> 16 & 255)) * share);
        var g = (int)((a >> 8 & 255) + ((int)(b >> 8 & 255) - (int)(a >> 8 & 255)) * share);
        var bl = (int)((a & 255) + ((int)(b & 255) - (int)(a & 255)) * share);
        return (uint)(r << 16 | g << 8 | bl);
    }

    // Layers of smooth noise, each finer and fainter than the last: the look of coastlines and cloud. Between 0 and 1.
    private static double Fbm(double x, double y, double z, int layers)
    {
        double sum = 0, weight = 0.5, total = 0;
        for (var i = 0; i < layers; i++)
        {
            sum += Noise(x, y, z) * weight;
            total += weight;
            (x, y, z) = (x * 2.03 + 11.3, y * 2.03 + 5.7, z * 2.03 + 3.1);
            weight *= 0.5;
        }
        return sum / total;
    }

    private static double Noise(double x, double y, double z)
    {
        var (xi, yi, zi) = ((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
        var (fx, fy, fz) = (Smooth(x - xi), Smooth(y - yi), Smooth(z - zi));
        double Corner(int dx, int dy, int dz) => Hash(xi + dx, yi + dy, zi + dz);
        var near = Lerp(Lerp(Corner(0, 0, 0), Corner(1, 0, 0), fx), Lerp(Corner(0, 1, 0), Corner(1, 1, 0), fx), fy);
        var far = Lerp(Lerp(Corner(0, 0, 1), Corner(1, 0, 1), fx), Lerp(Corner(0, 1, 1), Corner(1, 1, 1), fx), fy);
        return Lerp(near, far, fz);
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Hash(int x, int y, int z)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }
}

/// <summary>
/// A globe seen from in front and lit from the upper left, for drawing a planet from its map. For each dot of
/// the picture it holds where on the map that dot looks, how brightly it is lit, and how much air it looks
/// through at the edge; drawing a frame is then one pass over the dots. One is shared by every planet with the
/// same size and lean.
/// </summary>
public sealed class PlanetSphere
{
    private readonly int[] across;
    private readonly int[] down;
    private readonly float[] lit;
    private readonly float[] air;
    private readonly float[] cover;
    private readonly int mapWidth;

    public int Size { get; }

    /// <param name="size">The picture's width and height in dots.</param>
    /// <param name="lean">How far the planet's axis leans from upright, in radians.</param>
    /// <param name="mapWidth">The map's width; a power of two.</param>
    /// <param name="mapHeight">The map's height.</param>
    public PlanetSphere(int size, double lean, int mapWidth, int mapHeight)
    {
        if (mapWidth <= 0 || (mapWidth & (mapWidth - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(mapWidth), mapWidth, "The map's width must be a power of two.");
        Size = size;
        this.mapWidth = mapWidth;
        var count = size * size;
        (across, down, lit, air, cover) = (new int[count], new int[count], new float[count], new float[count], new float[count]);
        var radius = size / 2.0 - 1;
        var (sinLean, cosLean) = Math.SinCos(lean);
        // The light comes from the upper left and a little towards the viewer.
        var (lx, ly, lz) = (-0.52, -0.5, 0.69);
        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                var i = py * size + px;
                var x = (px + 0.5 - size / 2.0) / radius;
                var y = (py + 0.5 - size / 2.0) / radius;
                var out_ = Math.Sqrt(x * x + y * y);
                // A soft edge a dot wide, so the limb isn't jagged.
                var inside = Math.Clamp((1 - out_) * radius + 0.5, 0, 1);
                if (inside <= 0)
                    continue;
                var z = Math.Sqrt(Math.Max(0, 1 - Math.Min(1, x * x + y * y)));
                // Where this dot is on the leaning globe: undo the lean, then read off latitude and longitude.
                var ux = x * cosLean + y * sinLean;
                var uy = -x * sinLean + y * cosLean;
                var lat = Math.Asin(Math.Clamp(-uy, -1, 1));
                var lon = Math.Atan2(ux, z);
                // Longitude in 256ths of a map dot, so the turning is smooth and not a dot at a time.
                across[i] = (int)((lon / Math.Tau + 0.5) * mapWidth * 256);
                down[i] = Math.Clamp((int)((0.5 - lat / Math.PI) * mapHeight), 0, mapHeight - 1);
                var facing = x * lx + y * ly + z * lz;
                // Day fading into night over a soft band, never quite black on the day side's edge.
                var day = Math.Clamp((facing + 0.1) / 0.5, 0, 1);
                lit[i] = (float)(day * day * (3 - 2 * day) * (0.35 + 0.65 * Math.Clamp(facing, 0, 1)));
                // More air is looked through near the edge, and it only shines where the sun reaches it.
                air[i] = (float)(Math.Pow(1 - z, 2.4) * Math.Clamp(facing + 0.45, 0, 1));
                cover[i] = (float)inside;
            }
        }
    }

    /// <summary>
    /// Draws the globe into <paramref name="picture"/> (Size by Size, packed 0xAARRGGBB with the colour already
    /// multiplied by its cover, clear outside the globe). <paramref name="turned"/> is how far it has turned, in
    /// map dots; <paramref name="night"/> the colour its dark side sinks to; <paramref name="glow"/> its air's colour.
    /// </summary>
    public void Render(uint[] map, double turned, uint night, uint glow, int[] picture)
    {
        var turn = (int)Math.Floor(turned * 256);
        var mask = mapWidth - 1;
        var (nr, ng, nb) = ((int)(night >> 16 & 255), (int)(night >> 8 & 255), (int)(night & 255));
        var (gr, gg, gb) = ((int)(glow >> 16 & 255), (int)(glow >> 8 & 255), (int)(glow & 255));
        for (var i = 0; i < picture.Length; i++)
        {
            var a = cover[i];
            if (a <= 0)
            {
                picture[i] = 0;
                continue;
            }
            var place = across[i] + turn;
            var part = place & 255;
            var column = place >> 8;
            var row = down[i] * mapWidth;
            var first = map[row + (column & mask)];
            var second = map[row + ((column + 1) & mask)];
            // The two map dots either side, mixed by how far between them this is.
            var r = ((int)(first >> 16 & 255) * (256 - part) + (int)(second >> 16 & 255) * part) >> 8;
            var g = ((int)(first >> 8 & 255) * (256 - part) + (int)(second >> 8 & 255) * part) >> 8;
            var b = ((int)(first & 255) * (256 - part) + (int)(second & 255) * part) >> 8;
            var day = lit[i];
            var haze = air[i] * 0.75f;
            // Night is a little of the sky's own colour, so the dark side is a shape and not a hole.
            r = (int)(r * day + nr * 0.45f * (1 - day) + gr * haze);
            g = (int)(g * day + ng * 0.45f * (1 - day) + gg * haze);
            b = (int)(b * day + nb * 0.45f * (1 - day) + gb * haze);
            r = (int)(Math.Min(255, r) * a);
            g = (int)(Math.Min(255, g) * a);
            b = (int)(Math.Min(255, b) * a);
            picture[i] = (int)(a * 255) << 24 | r << 16 | g << 8 | b;
        }
    }
}
