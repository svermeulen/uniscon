using System;

namespace Uniscon.Internal
{
    public class UnisconAssertException : Exception
    {
        public UnisconAssertException(string message)
            : base(message) { }

        public UnisconAssertException(string format, params object[] formatArgs)
            : base(string.Format(format, formatArgs)) { }
    }

    public static class Assert
    {
        public static void That(bool condition)
        {
            if (!condition)
            {
                throw CreateException("Assert hit!");
            }
        }

        public static void That(bool condition, string message)
        {
            if (!condition)
            {
                throw CreateException(message);
            }
        }

        public static void That<T>(bool condition, string message, T arg1)
        {
            if (!condition)
            {
                throw CreateException(message, arg1!);
            }
        }

        static UnisconAssertException CreateException()
        {
            return new UnisconAssertException("Assert hit!");
        }

        public static UnisconAssertException CreateException(string message, params object[] args)
        {
            return new UnisconAssertException("Assert hit!  Details: {0}", string.Format(message, args));
        }
    }
}
