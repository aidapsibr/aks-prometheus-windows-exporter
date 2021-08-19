using System;

namespace WindowsPrometheusSync
{
    internal static class Utilities
    {
        private static readonly string _innerExceptionSeperator =
            Environment.NewLine + Environment.NewLine + "--Inner Exception--" + Environment.NewLine;

        public static string WalkExWithType(Exception ex)
        {
            return
                $"Type: {ex.GetType().FullName} - Message: {ex.Message} -  HResult: {ex.HResult} - Stack Trace: {Environment.NewLine}{ex.StackTrace} {(ex.InnerException == null ? string.Empty : _innerExceptionSeperator + WalkExWithType(ex.InnerException))}";
        }

        public static string WalkWithType(this Exception ex)
        {
            if (ex == null) return null;

            return WalkExWithType(ex);
        }
    }
}