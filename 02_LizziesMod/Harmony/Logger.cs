namespace LizziesMod
{
    public static class Logger
    {

        public static string lastOutput = "";
        public static int counter = 0;

        public static void Info(string message)
        {
            
            if(lastOutput == message)
            {
                counter++;
                return;
            }

            if (lastOutput.Length != 0 && lastOutput != message && counter > 0) {
                Log.Out("       Last message repeated " + counter + " times");
                counter = 0;
            };

            Log.Out("[LizziesMod] " + message);
            lastOutput = message;
        }

        public static void Warning(string message)
        {

            Log.Warning("[LizziesMod] " + message);
        }

        public static void Error(string message)
        {
            Log.Error("[LizziesMod] " + message);
        }
    }
}