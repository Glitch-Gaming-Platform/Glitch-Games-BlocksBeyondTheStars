using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Remappable, discrete input actions, each resolved to a <see cref="KeyCode"/> through the player's
    /// bindings. This is the seed set for the controls-remapping work (Stream C): the legacy hardcoded
    /// <c>Input.GetKey(KeyCode.X)</c> call sites migrate onto these one subsystem at a time, and a settings UI
    /// can then rebind them. Continuous movement axes still go through the legacy Input Manager for now.
    /// </summary>
    public enum InputAction
    {
        Interact,          // generic "use / board / open" — default E
        PrimaryFire,       // melee swing / fire the held weapon — default F
        StowVehicle,       // pack up a deployed speeder you're standing next to — default X
        ToggleThirdPerson, // switch first/third-person camera — default V
        LootContainer,     // loot the nearest container — default G
        DepositToCrate,    // deposit into the nearest storage crate — default H
        RepairWreck,       // repair the nearest wreck cell (on foot) — default R
        ToggleLamp,        // toggle the suit lamp — default L
    }

    /// <summary>
    /// Central indirection over Unity's legacy Input so every key flows through the player's bindings
    /// (<see cref="ClientSettings"/>) instead of a hardcoded <see cref="KeyCode"/> — the foundation for
    /// rebindable controls. Call <see cref="Use"/> once at startup with the loaded settings; an unbound action
    /// falls back to <see cref="DefaultKey"/> (the key it had before remapping existed), so migrating a call
    /// site is behaviour-preserving until the player actually rebinds it.
    /// </summary>
    public static class InputMap
    {
        private static ClientSettings _settings;

        /// <summary>The actions exposed in the controls-rebinding UI, in display order.</summary>
        public static readonly InputAction[] Remappable =
        {
            InputAction.Interact, InputAction.PrimaryFire, InputAction.StowVehicle,
            InputAction.ToggleThirdPerson, InputAction.LootContainer, InputAction.DepositToCrate,
            InputAction.RepairWreck, InputAction.ToggleLamp,
        };

        /// <summary>Points the map at the active settings (called once after <c>ClientSettings.Load()</c>).</summary>
        public static void Use(ClientSettings settings) => _settings = settings;

        /// <summary>The built-in default key for an action (its binding before remapping existed).</summary>
        public static KeyCode DefaultKey(InputAction action) => action switch
        {
            InputAction.Interact => KeyCode.E,
            InputAction.PrimaryFire => KeyCode.F,
            InputAction.StowVehicle => KeyCode.X,
            InputAction.ToggleThirdPerson => KeyCode.V,
            InputAction.LootContainer => KeyCode.G,
            InputAction.DepositToCrate => KeyCode.H,
            InputAction.RepairWreck => KeyCode.R,
            InputAction.ToggleLamp => KeyCode.L,
            _ => KeyCode.None,
        };

        /// <summary>The currently bound key for an action — the player's override if set, else the default.</summary>
        public static KeyCode Key(InputAction action)
        {
            var def = DefaultKey(action);
            if (_settings == null)
            {
                return def;
            }

            string name = _settings.BoundKeyName(action.ToString());
            return !string.IsNullOrEmpty(name) && System.Enum.TryParse<KeyCode>(name, out var kc) ? kc : def;
        }

        public static bool Down(InputAction action) => Input.GetKeyDown(Key(action));
        public static bool Held(InputAction action) => Input.GetKey(Key(action));
        public static bool Up(InputAction action) => Input.GetKeyUp(Key(action));
    }
}
