using VRage.Utils;

namespace Fd32.ModUI
{
    public static class Log
    {
        public static void Write(string text)
        {
            MyLog.Default.WriteLine("ModUI| " + text);
        }
    }
}
