using p4gpc.inputlibrary.Configuration;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using Reloaded.Hooks.Definitions.Enums;
using Reloaded.Memory.Sources;
using Reloaded.Mod.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static Reloaded.Hooks.Definitions.X86.FunctionAttribute;
using static p4gpc.inputlibrary.Logging;
using p4gpc.inputlibrary.interfaces;

namespace p4gpc.inputlibrary
{
    public unsafe class Inputs : IInputHook
    {
        private IReloadedHooks _hooks;
        private IMemory _memory; // For accessing memory
        private int _baseAddress; // Base address (probably won't ever change)
        // For calling C# code from ASM.
        private IReverseWrapper<KeyboardInputFunction> _keyboardReverseWrapper;
        private IReverseWrapper<ControllerInputFunction> _controllerReverseWrapper;
        // For maniplulating input reading hooks
        private IAsmHook _keyboardHook;
        private IAsmHook _controllerHook;
        // Keeps track of the last inputs for rising/falling edge detection
        private int[] controllerInputHistory = new int[10];
        private int lastControllerInput = 0;
        private int lastKeyboardInput = 0;
        private Config _config { get; set; }
        private Logging _utils;
        public Inputs(IReloadedHooks hooks, Config configuration, Logging utils, int baseAddress, IMemory memory)
        {
            // Initialise private variables
            _config = configuration;
            _hooks = hooks;
            _memory = memory;
            _utils = utils;
            _baseAddress = baseAddress;

            // Create input hook
            _utils.Log("Hooking into input functions");
            try
            {
                // Define functions (they're the same but use different reverse wrappers)
                string[] keyboardFunction =
                {
                    $"use32",
                    // Not always necessary but good practice;
                    // just in case the parent function doesn't preserve them.
                    $"{hooks.Utilities.PushCdeclCallerSavedRegisters()}",
                    $"{hooks.Utilities.GetAbsoluteCallMnemonics(KeyboardInputHappened, out _keyboardReverseWrapper)}",
                    $"{hooks.Utilities.PopCdeclCallerSavedRegisters()}",
                };
                string[] controllerFunction =
                {
                    $"use32",
                    // Not always necessary but good practice;
                    // just in case the parent function doesn't preserve them.
                    $"{hooks.Utilities.PushCdeclCallerSavedRegisters()}",
                    $"{hooks.Utilities.GetAbsoluteCallMnemonics(ControllerInputHappened, out _controllerReverseWrapper)}",
                    $"{hooks.Utilities.PopCdeclCallerSavedRegisters()}",
                };

                // Create function hooks
                long keyboardAddress = -1, controllerAddress = -1;
                List<Task> hookSigScans = new List<Task>();
                hookSigScans.Add(Task.Run(() => keyboardAddress = _utils.SigScan("85 DB 74 05 E8 ?? ?? ?? ?? 8B 7D F8", "keyboard hook")));
                hookSigScans.Add(Task.Run(() => controllerAddress = _utils.SigScan("0F AB D3 89 5D C8", "controller hook")));
                Task.WaitAll(hookSigScans.ToArray());

                if (keyboardAddress != -1 && controllerAddress != -1)
                {
                    _keyboardHook = hooks.CreateAsmHook(keyboardFunction, keyboardAddress, AsmHookBehaviour.ExecuteFirst).Activate();
                    _controllerHook = hooks.CreateAsmHook(controllerFunction, controllerAddress, AsmHookBehaviour.ExecuteFirst).Activate();
                    _utils.Log("Successfully hooked into input functions");
                }
                else
                {
                    _utils.LogError($"Unable to find input functions to hook into. Additions that rely on inputs will not work");
                }
            }
            catch (Exception e)
            {
                _utils.LogError($"Error hooking into input functions. Additions that rely on inputs will not work", e);
            }
        }
        private void InputHappened(int input, bool risingEdge, bool keyboard)
        {
            InvokeOnInput(input, risingEdge, keyboard);
            _utils.LogDebug($"Input was {(Input)input} and was {(risingEdge ? "rising" : "falling")} edge");
        }

        // Get keyboard inputs
        private void KeyboardInputHappened(int input)
        {
            // Initialise item location once inputs start being received 

            // Switch cross and circle as it is opposite compared to controller
            if (input == (int)Input.Circle) input = (int)Input.Cross;
            else if (input == (int)Input.Cross) input = (int)Input.Circle;
            // Decide whether the input needs to be processed (only rising edge for now)
            if (RisingEdge(input, lastKeyboardInput))
                InputHappened(input, true, true);
            else if (FallingEdge(input, lastKeyboardInput))
                InputHappened(input, false, true);
            // Update the last inputs
            lastKeyboardInput = input;
            if (controllerInputHistory[0] == 0)
            {
                if (lastControllerInput != 0)
                    InputHappened(input, false, false);
                lastControllerInput = 0;
            }
            _utils.ArrayPush(controllerInputHistory, 0);
        }

        // Gets controller inputs
        private void ControllerInputHappened(int input)
        {
            // Get the input
            _utils.ArrayPush(controllerInputHistory, input);
            input = GetControllerInput();
            // Decide whether the input needs to be processed
            if (RisingEdge(input, lastControllerInput))
                InputHappened(input, true, false);
            // Update last input
            lastControllerInput = input;
        }

        // Checks if an input was rising edge (the button was just pressed)
        private bool RisingEdge(int currentInput, int lastInput)
        {
            if (currentInput == 0) return false;
            return currentInput != lastInput;
        }

        // Checks if an input was falling edge (the button was let go of)
        private bool FallingEdge(int currentInput, int lastInput)
        {
            return lastInput != 0 && currentInput != lastInput;
        }

        // Gets controller input returning an input combo int if a combo was done (like what keyboard produces)
        private int GetControllerInput()
        {
            int inputCombo = 0;
            int lastInput = 0;
            // Work out the pressed buttons
            for (int i = 0; i < controllerInputHistory.Length; i++)
            {
                int input = controllerInputHistory[i];
                // Start of a combo
                if (lastInput == 0 && input != 0)
                    inputCombo = input;
                // Middle of a combo
                else if (lastInput != 0 && input != 0)
                    inputCombo += input;
                // End of a combo
                else if (input == 0 && lastInput != 0 && i != 1)
                    break;
                // Two 0's in a row means the combo must be over
                else if (i != 0 && input == 0 && lastInput == 0)
                    break;
                lastInput = input;
            }
            return inputCombo;
        }

        // Works out what inputs were pressed if a combination of keys were pressed (only applicable to keyboard)
        private List<Input> GetInputsFromCombo(int inputCombo, bool keyboard)
        {
            // List of the inputs found in the combo
            List<Input> foundInputs = new List<Input>();
            // Check if the input isn't actually a combo, if so we can directly return it
            if (Enum.IsDefined(typeof(Input), inputCombo))
            {
                // Switch cross and circle if it is one of them as it is opposite compared to controller
                if (keyboard && inputCombo == (int)Input.Circle)
                    foundInputs.Add(Input.Cross);
                else if (keyboard && inputCombo == (int)Input.Cross)
                    foundInputs.Add(Input.Circle);
                else
                    foundInputs.Add((Input)inputCombo);
                return foundInputs;
            }

            // Get all possible inputs as an array
            var possibleInputs = Enum.GetValues(typeof(Input));
            // Reverse the array so it goes from highest input value to smallest
            Array.Reverse(possibleInputs);
            // Go through each possible input to find out which are a part of the key combo
            foreach (int possibleInput in possibleInputs)
            {
                // If input - possibleInput is greater than 0 that input must be a part of the combination
                // This is the same idea as converting bits to decimal
                if (inputCombo - possibleInput >= 0)
                {
                    inputCombo -= possibleInput;
                    // Switch cross and circle if it is one of them as it is opposite compared to controller
                    if (keyboard && possibleInput == (int)Input.Circle)
                        foundInputs.Add(Input.Cross);
                    else if (keyboard && possibleInput == (int)Input.Cross)
                        foundInputs.Add(Input.Circle);
                    else
                        foundInputs.Add((Input)possibleInput);
                }
            }
            if (foundInputs.Count > 0)
                _utils.LogDebug($"Input combo was {string.Join(", ", foundInputs)}");
            return foundInputs;
        }

        [Function(Register.ebx, Register.edi, StackCleanup.Callee)]
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void KeyboardInputFunction(int input);

        [Function(Register.eax, Register.edi, StackCleanup.Callee)]
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ControllerInputFunction(int input);

        /* IInputHook Interface */
        public event OnInputEvent OnInput;

        public void InvokeOnInput(int inputs, bool risingEdge, bool controlType) => OnInput?.Invoke(inputs, risingEdge, controlType);
    }
}
