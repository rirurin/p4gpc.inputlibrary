using p4gpc.inputlibrary.Configuration;
using Reloaded.Memory.Sources;
using Reloaded.Memory.Sigscan;
using Reloaded.Mod.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace p4gpc.inputlibrary
{
    public class Logging
    {
        public Config Configuration;
        private ILogger _logger;
        private int _baseAddress;
        private IMemory _memory;
        public Logging(Config configuration, ILogger logger, int baseAddress, IMemory memory)
        {
            // Initialise fields
            Configuration = configuration;
            _logger = logger;
            _baseAddress = baseAddress;
            _memory = memory;
        }
        public enum Input
        {
            Select = 0x1,
            Start = 0x8,
            Up = 0x10,
            Right = 0x20,
            Down = 0x40,
            Left = 0x80,
            LB = 0x400,
            RB = 0x800,
            Triangle = 0x1000,
            Circle = 0x2000,
            Cross = 0x4000,
            Square = 0x8000
        };

        public void LogDebug(string message)
        {
            if (Configuration.DebugEnabled)
                _logger.WriteLine($"[InputLibrary] {message}");
        }

        public void Log(string message)
        {
            _logger.WriteLine($"[InputLibrary] {message}");
        }

        public void LogError(string message, Exception e)
        {
            _logger.WriteLine($"[InputLibrary] {message}: {e.Message}", System.Drawing.Color.Red);
        }

        public void LogError(string message)
        {
            _logger.WriteLine($"[InputLibrary] {message}", System.Drawing.Color.Red);
        }

        // Signature Scans for a location in memory, returning -1 if the scan fails otherwise the address
        public long SigScan(string pattern, string functionName)
        {
            try
            {
                using var thisProcess = Process.GetCurrentProcess();
                using var scanner = new Scanner(thisProcess, thisProcess.MainModule);
                long functionAddress = scanner.CompiledFindPattern(pattern).Offset;
                if (functionAddress < 0) throw new Exception($"Unable to find bytes with pattern {pattern}");
                functionAddress += _baseAddress;
                LogDebug($"Found the {functionName} address at 0x{functionAddress:X}");
                return functionAddress;
            }
            catch (Exception exception)
            {
                LogError($"An error occured trying to find the {functionName} function address. Not initializing. Please report this with information on the version of P4G you are running", exception);
                return -1;
            }
        }
        // Pushes an item to the beginning of the array, pushing everything else forward and removing the last element
        public void ArrayPush<T>(T[] array, T newItem)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                array[i] = array[i - 1];
            }
            array[0] = newItem;
        }
    }
}
