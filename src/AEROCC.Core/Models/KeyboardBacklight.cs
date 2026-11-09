namespace AEROCC.Core.Models;

public enum KeyboardEffect : byte
{
    Static = 1,
    Breathing = 2,
    Wave = 3,
    Neon = 4,
    Sunset = 5,
    SunsetBreathing = 6,
    SunsetNeon = 7,
    Custom = 9
}

public enum KeyboardColor : byte
{
    Off = 0,
    Red = 1,
    Green = 2,
    Yellow = 3,
    Blue = 4,
    Orange = 5,
    Purple = 6,
    White = 7,
    Random = 8
}

public class KeyboardBacklightStatus
{
    public KeyboardEffect Effect { get; set; } = KeyboardEffect.Static;
    public int Speed { get; set; } = 5;
    public int Brightness { get; set; } = 100; // 0 - 100%
    public KeyboardColor Color { get; set; } = KeyboardColor.White;
    public int Direction { get; set; } = 1;

    public override string ToString() =>
        $"Effect: {Effect}, Brightness: {Brightness}%, Color: {Color}, Speed: {Speed}";
}
