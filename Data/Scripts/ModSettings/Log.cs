using VRage.Utils;

namespace ModSettings
{
    public static class Log
    {
        public static void Write(string text)
        {
            MyLog.Default.WriteLine("ModSettings| " + text);
        }
    }
}
