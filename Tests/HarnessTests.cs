using System.Linq;
using System.Reflection;
using ChronoSave.Core;
using NUnit.Framework;
using Verse;

namespace ChronoSave.Tests
{
    /// <summary>
    /// Establishes what this harness is actually running against, and records the boundary that
    /// keeps the rest of the mod out of reach.
    /// </summary>
    /// <remarks>
    /// These are not tests of the mod's behaviour. They exist so that a change to the harness that
    /// quietly stops loading the real game assemblies, or that removes the seam the scheduling
    /// tests depend on, fails here rather than somewhere confusing.
    /// </remarks>
    [TestFixture]
    public class HarnessTests
    {
        [Test]
        public void RunsAgainstTheRealGameAssembly()
        {
            // If this ever resolves to a stub, every other test in the suite is measuring nothing.
            var assembly = typeof(ModSettings).Assembly.GetName().Name;
            Assert.That(assembly, Is.EqualTo("Assembly-CSharp"));
        }

        [Test]
        public void SettingsCarryTheDocumentedDefaults()
        {
            // Constructing a ModSettings subclass outside the game works: the base class touches no
            // static game state. The defaults are the ones About.xml and the store pages quote.
            var settings = new ChronoSaveSettings();

            Assert.That(settings.SaveIntervalMinutes, Is.EqualTo(5f));
            Assert.That(settings.NumberOfSaves, Is.EqualTo(10));
            Assert.That(settings.ChronoSaveEnabled, Is.True);
        }

        [Test]
        public void TheGameComponentConstructsWithoutAGame()
        {
            // The constructor ignores its Game argument entirely, which is what makes any direct
            // testing of the component possible at all.
            Assert.That(new ChronoSaveGameComponent(null), Is.Not.Null);
        }

        [Test]
        public void TheGameComponentStillCannotBeExercisedWithoutTheModSettingsStatic()
        {
            // This is the boundary, pinned deliberately. ChronoSaveGameComponent reads its settings
            // through the static ChronoSaveMod.Settings, which only the game populates, so every
            // method that consults them throws out here. That is why the scheduling decisions were
            // extracted into ChronoSaveSchedule instead of being tested through the component.
            //
            // If someone makes the settings injectable, this test starts failing. Delete it then,
            // and test the component directly.
            var component = new ChronoSaveGameComponent(null);
            var method = typeof(ChronoSaveGameComponent)
                .GetMethod("ChooseSaveName", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, "ChooseSaveName was renamed or removed.");
            Assert.That(
                () => method.Invoke(component, null),
                Throws.InnerException.TypeOf<System.NullReferenceException>());
        }

        [Test]
        public void TheRotationSlotIsNotAFieldAndSoCannotBeScribed()
        {
            // The critical defect this mod had. currentSaveIndex was a field on a GameComponent, and
            // Verse.Game.ExposeSmallComponents deep-scribes components, so the slot was written into
            // every save the game produced while the mod was active, manual saves and vanilla
            // autosaves included. Loading any of them rewound the ring and the mod then overwrote
            // forward over newer chronosaves.
            //
            // Reintroducing any rotation state as a field would reintroduce that, so this asserts
            // there is no instance field on the component holding an int at all. The slot is derived
            // from the saves folder at the moment of writing, which is what vanilla's autosaver does.
            var intFields = typeof(ChronoSaveGameComponent)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(f => f.FieldType == typeof(int))
                .Select(f => f.Name)
                .ToArray();

            Assert.That(intFields, Is.Empty, "A rotation slot on the component is scribed into every save.");
        }
    }
}
