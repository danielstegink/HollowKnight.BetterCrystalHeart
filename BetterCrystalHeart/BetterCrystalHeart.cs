using DanielSteginkUtils.Utilities;
using Modding;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCrystalHeart
{
    public class BetterCrystalHeart : Mod, IGlobalSettings<GlobalSettings>, ICustomMenuMod
    {
        public static BetterCrystalHeart Instance;

        public override string GetVersion() => "1.1.1.0";

        #region Save Settings
        internal static GlobalSettings globalSettings = new GlobalSettings();

        public void OnLoadGlobal(GlobalSettings s)
        {
            globalSettings = s;
        }

        public GlobalSettings OnSaveGlobal()
        {
            return globalSettings;
        }
        #endregion

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            Log("Initializing");

            Instance = this;

            On.HutongGames.PlayMaker.Actions.ListenForSuperdash.OnEnter += RemoveDelay;
            On.HutongGames.PlayMaker.Actions.Wait.OnEnter += ChargeTime;
            On.HealthManager.Start += StoreMaxHealth;
            On.HealthManager.TakeDamage += Damage;

            Log("Initialized");
        }

        /// <summary>
        /// Removes the small/confusing delay before charge-up occurs
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        private void RemoveDelay(On.HutongGames.PlayMaker.Actions.ListenForSuperdash.orig_OnEnter orig, HutongGames.PlayMaker.Actions.ListenForSuperdash self)
        {
            orig(self);

            if (self.Fsm.Name.Contains("Superdash") &&
                self.State.Name.Equals("Inactive"))
            {
                self.isPressed = self.wasPressed;
            }
        }

        #region ChargeTime
        /// <summary>
        /// Stores the original wait time for CDash
        /// </summary>
        private float originalValue = -1;

        /// <summary>
        /// The greatest weakeness of Crystal Heart as an attack is its charge time.
        /// Equipping Dashmaster, Sprintmaster or Quick Focus reduces its charge time.
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        private void ChargeTime(On.HutongGames.PlayMaker.Actions.Wait.orig_OnEnter orig, HutongGames.PlayMaker.Actions.Wait self)
        {
            // Make sure this is the CDash FSM
            if (self.Fsm.Name.Equals("Superdash"))
            {
                // Caches modified value for easy reset
                if (originalValue < 0)
                {
                    originalValue = self.time.Value;
                }

                // Make sure this is one of the charge-up states
                if (self.State.Name.Equals("Wall Charge") ||
                    self.State.Name.Equals("Ground Charge"))
                {
                    // We can't reset the charge time until after the Wait action is finished
                    // So its easier to cache it beforehand and use the original value as a base each time
                    float timeModifier = GetTimeModifier();
                    self.time.Value = originalValue * timeModifier;
                    //Log($"CDash charge time: {originalValue} * {timeModifier} = {self.time.Value}");
                }

                orig(self);
            }
            else
            {
                orig(self);
            }
        }

        /// <summary>
        /// Gets the percent decrease in charge time
        /// </summary>
        /// <returns></returns>
        private float GetTimeModifier()
        {
            // Although Crystal Dash instinctively feels like a dash, it is actually closer to a nail art
            // NMG costs 1 notch to reduce the charge time of nail arts by 44%
            float speedBoostPerNotch = 1 - 0.75f / 1.35f;
            //Log($"CDash charge time: default boost {speedBoostPerNotch}");

            // A little personal adjustment based on my tastes
            speedBoostPerNotch /= 2f;

            // Since this is a synergy rather than the intended effect of the charms,
            // let's say it takes 3 notches worth of charms to achieve 1 notch of cooldown
            float notchValue = 0;

            // Sprintmaster costs 1 notch
            if (PlayerData.instance.GetBool("equippedCharm_37"))
            {
                notchValue += 1;
            }

            // Dashmaster costs 2 notches
            if (PlayerData.instance.GetBool("equippedCharm_31"))
            {
                notchValue += 2;
            }

            // Quick Focus costs 3 notches
            if (PlayerData.instance.GetBool("equippedCharm_7"))
            {
                notchValue += 3;
            }

            float modifier = speedBoostPerNotch * notchValue / 3f;
            //Log($"CDash charge time: {speedBoostPerNotch} * {notchValue} / 3 = {modifier}");

            // Apply user-set percentile bonus (or penalty)
            float globalModifier = 1 + (globalSettings.TimeModifier / 100); // 100% to double, -100% to negate
            modifier *= globalModifier;

            return 1 - modifier;
        }
        #endregion

        #region StoreMaxHealth
        /// <summary>
        /// Stores the max health of enemies
        /// </summary>
        private Dictionary<string, int> enemyMaxHealth = new Dictionary<string, int>();

        /// <summary>
        /// Stores max health of enemies to help Damage calculations
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        private void StoreMaxHealth(On.HealthManager.orig_Start orig, HealthManager self)
        {
            orig(self);
            StoreMaxHealth(self);
        }

        /// <summary>
        /// Stores max health in a data structure
        /// </summary>
        /// <param name="self"></param>
        private void StoreMaxHealth(HealthManager self)
        {
            GameObject enemy = self.gameObject;
            string enemyName = enemy.name;
            if (!enemyMaxHealth.ContainsKey(enemyName))
            {
                enemyMaxHealth.Add(enemyName, self.hp);
                //Log($"{enemyName} has a max HP of {self.hp}");
            }
        }
        #endregion

        #region Damage
        /// <summary>
        /// Crystal Heart also deals very little damage for a suicide attack.
        /// Damages enemies for a percentage of their health, magnified by equipping Deep Focus.
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        /// <param name="hitInstance"></param>
        private void Damage(On.HealthManager.orig_TakeDamage orig, HealthManager self, HitInstance hitInstance)
        {
            if (hitInstance.Source.name.Equals("SuperDash Damage"))
            {
                int bonusDamage = (int)GetDamageBonus(self);
                hitInstance.DamageDealt += bonusDamage;
                //Log($"CDash damage increased by {bonusDamage} to {hitInstance.DamageDealt}");
            }

            orig(self, hitInstance);
        }

        /// <summary>
        /// Gets the damage bonus for CDash
        /// </summary>
        /// <returns></returns>
        private float GetDamageBonus(HealthManager enemy)
        {
            float bonusDamage = GetRevengeDamage(enemy);

            // It also makes some sense to buff CDash based on our nail, since Sharp Shadow works that way
            bonusDamage += PlayerData.instance.GetInt("nailDamage");

            bonusDamage *= GetCharmBonus();
            //Log($"CDash charm damage: {charmModifier} -> {bonusDamage}");

            // A little personal adjustment based on my tastes
            bonusDamage /= 1.5f;
            //Log($"CDash final damage: {bonusDamage}");

            // Apply user-set percentile bonus (or penalty)
            float globalModifier = 1 + (globalSettings.DamageModifier / 100); // 100% to double, -100% to negate
            bonusDamage *= globalModifier;

            return bonusDamage;
        }

        /// <summary>
        /// We take damage whenever we deal damage with CDash, so we will hit the enemy for a portion of their max health.
        /// </summary>
        /// <param name="enemy"></param>
        /// <returns></returns>
        private float GetRevengeDamage(HealthManager enemy)
        {
            float maxHealth = PlayerData.instance.GetInt("maxHealth");
            float healthPercent = 1 / maxHealth;

            if (!enemyMaxHealth.ContainsKey(enemy.gameObject.name))
            {
                StoreMaxHealth(enemy);
            }
            int enemyHealth = enemyMaxHealth[enemy.gameObject.name];

            float bonusDamage = enemyHealth * healthPercent;
            //Log($"CDash damage: {enemyHealth} * {healthPercent} = {bonusDamage}");
            return bonusDamage;
        }

        /// <summary>
        /// Gets the damage modifier to apply based on charms equipped
        /// </summary>
        /// <returns></returns>
        private float GetCharmBonus()
        {
            // Crystal Dash charges like a Nail Art, so it makes sense to buff its damage like one
            // Per my Utils, 1 notch is worth a 27% increase in Nail Art damage
            float damageBoostPerNotch = NotchCosts.NailArtDamagePerNotch();

            // Since this is a synergy, let's say it takes 3 notches worth of charms to achieve 1 notch of damage boost
            float notchValue = 0;

            // Deep Focus is worth 4 notches
            if (PlayerData.instance.GetBool("equippedCharm_34"))
            {
                notchValue += 4;
            }

            return 1 + damageBoostPerNotch * notchValue / 3;
        }
        #endregion

        #region Menu Options
        public bool ToggleButtonInsideMenu => false;

        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? modToggleDelegates)
        {
            return ModMenu.CreateMenuScreen(modListMenu);
        }
        #endregion
    }
}