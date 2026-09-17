using System.Globalization;
using System.Reflection;
using System.Threading;
using ChronoSave.Core;
using NUnit.Framework;

namespace ChronoSave.Tests
{
    /// <summary>
    /// Covers the settings arithmetic: clamping, and reading and writing the interval field's edit
    /// buffer.
    /// </summary>
    /// <remarks>
    /// The window itself stays out of reach. <c>Listing_Standard</c> has a
    /// <c>[StaticConstructorOnStartup]</c> that loads textures, and <c>Widgets.TextFieldNumeric</c>
    /// goes straight into IMGUI. What is reachable, and what was actually wrong, is the arithmetic
    /// around the field.
    /// </remarks>
    [TestFixture]
    public class ChronoSaveSettingsTests
    {
        [Test]
        public void ClampIntervalMinutes_LeavesAValueInsideTheRangeAlone()
        {
            Assert.That(ChronoSaveSchedule.ClampIntervalMinutes(1f), Is.EqualTo(1f));
            Assert.That(ChronoSaveSchedule.ClampIntervalMinutes(5f), Is.EqualTo(5f));
            Assert.That(ChronoSaveSchedule.ClampIntervalMinutes(60f), Is.EqualTo(60f));
        }

        [Test]
        public void ClampIntervalMinutes_ClampsOutsideTheRange()
        {
            Assert.That(ChronoSaveSchedule.ClampIntervalMinutes(0f), Is.EqualTo(ChronoSaveSchedule.MinIntervalMinutes));
            Assert.That(ChronoSaveSchedule.ClampIntervalMinutes(-3f), Is.EqualTo(ChronoSaveSchedule.MinIntervalMinutes));
            Assert.That(ChronoSaveSchedule.ClampIntervalMinutes(70f), Is.EqualTo(ChronoSaveSchedule.MaxIntervalMinutes));
        }

        [Test]
        public void ClampIntervalMinutes_ReplacesNotANumberWithTheDefault()
        {
            // A NaN interval makes every comparison in IsDue false, so chronosaving would stop with
            // nothing anywhere to say why. Mathf.Clamp passes NaN straight through, which is one
            // reason this is written as plain comparisons.
            Assert.That(ChronoSaveSchedule.ClampIntervalMinutes(float.NaN), Is.EqualTo(ChronoSaveSchedule.DefaultIntervalMinutes));
        }

        [Test]
        public void ClampIntervalMinutes_ClampsInfinities()
        {
            Assert.That(ChronoSaveSchedule.ClampIntervalMinutes(float.PositiveInfinity), Is.EqualTo(ChronoSaveSchedule.MaxIntervalMinutes));
            Assert.That(ChronoSaveSchedule.ClampIntervalMinutes(float.NegativeInfinity), Is.EqualTo(ChronoSaveSchedule.MinIntervalMinutes));
        }

        [Test]
        public void ClampSlotCount_LeavesAValueInsideTheRangeAlone()
        {
            Assert.That(ChronoSaveSchedule.ClampSlotCount(1), Is.EqualTo(1));
            Assert.That(ChronoSaveSchedule.ClampSlotCount(10), Is.EqualTo(10));
            Assert.That(ChronoSaveSchedule.ClampSlotCount(25), Is.EqualTo(25));
        }

        [Test]
        public void ClampSlotCount_ClampsOutsideTheRange()
        {
            Assert.That(ChronoSaveSchedule.ClampSlotCount(0), Is.EqualTo(ChronoSaveSchedule.MinSlots));
            Assert.That(ChronoSaveSchedule.ClampSlotCount(-5), Is.EqualTo(ChronoSaveSchedule.MinSlots));
            Assert.That(ChronoSaveSchedule.ClampSlotCount(26), Is.EqualTo(ChronoSaveSchedule.MaxSlots));
        }

        [Test]
        public void ParseIntervalBuffer_KeepsTheCurrentValueForAnEmptyBuffer()
        {
            // The clear-and-retype fix. An empty buffer must not reset the interval, because the
            // field has to tolerate being emptied while the player types a new number into it.
            Assert.That(ChronoSaveSchedule.ParseIntervalBuffer(string.Empty, 7f), Is.EqualTo(7f));
            Assert.That(ChronoSaveSchedule.ParseIntervalBuffer(null, 7f), Is.EqualTo(7f));
        }

        [Test]
        public void ParseIntervalBuffer_KeepsTheCurrentValueForGarbage()
        {
            Assert.That(ChronoSaveSchedule.ParseIntervalBuffer("   ", 7f), Is.EqualTo(7f));
            Assert.That(ChronoSaveSchedule.ParseIntervalBuffer("abc", 7f), Is.EqualTo(7f));
            Assert.That(ChronoSaveSchedule.ParseIntervalBuffer("-", 7f), Is.EqualTo(7f));
        }

        [Test]
        public void ParseIntervalBuffer_ClampsTheFallbackToo()
        {
            // A fallback out of range would otherwise leak a bad value straight back out.
            Assert.That(ChronoSaveSchedule.ParseIntervalBuffer(null, 999f), Is.EqualTo(ChronoSaveSchedule.MaxIntervalMinutes));
        }

        [Test]
        public void ParseIntervalBuffer_ParsesAndClamps()
        {
            Assert.That(ChronoSaveSchedule.ParseIntervalBuffer("70", 5f), Is.EqualTo(ChronoSaveSchedule.MaxIntervalMinutes));
            Assert.That(ChronoSaveSchedule.ParseIntervalBuffer("0", 5f), Is.EqualTo(ChronoSaveSchedule.MinIntervalMinutes));
            Assert.That(ChronoSaveSchedule.ParseIntervalBuffer("2.5", 5f), Is.EqualTo(2.5f));
        }

        [Test]
        public void ParseIntervalBuffer_IsCultureInvariant()
        {
            // The one way this could be quietly wrong for a large part of the player base. Under a
            // comma-decimal culture a naive parse reads "2.5" as 25.
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                Assert.That(ChronoSaveSchedule.ParseIntervalBuffer("2.5", 5f), Is.EqualTo(2.5f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void FormatIntervalBuffer_DropsTrailingZeros()
        {
            Assert.That(ChronoSaveSchedule.FormatIntervalBuffer(5f), Is.EqualTo("5"));
            Assert.That(ChronoSaveSchedule.FormatIntervalBuffer(2.5f), Is.EqualTo("2.5"));
            Assert.That(ChronoSaveSchedule.FormatIntervalBuffer(60f), Is.EqualTo("60"));
        }

        [Test]
        public void FormatIntervalBuffer_IsCultureInvariant()
        {
            // A comma here would be rejected by vanilla's own numeric field, which admits only
            // digits, a minus and a full stop, so the player's saved value would be refused on a
            // German or French machine.
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                Assert.That(ChronoSaveSchedule.FormatIntervalBuffer(2.5f), Is.EqualTo("2.5"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void ParseThenFormat_RoundTrips()
        {
            foreach (var value in new[] { 1f, 2.5f, 5f, 30f, 60f })
            {
                Assert.That(
                    ChronoSaveSchedule.ParseIntervalBuffer(ChronoSaveSchedule.FormatIntervalBuffer(value), 0f),
                    Is.EqualTo(value),
                    value + " does not survive a round trip through the edit buffer.");
            }
        }

        [Test]
        public void CommitEditBuffers_RefillsAnEmptyIntervalBuffer()
        {
            // The other half of the clear-and-retype fix: the field may be left empty while the
            // window is open, but not once it closes, or it reopens empty.
            var settings = new ChronoSaveSettings();
            var buffer = typeof(ChronoSaveSettings).GetField("saveIntervalBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(buffer, Is.Not.Null, "saveIntervalBuffer was renamed or removed.");

            buffer.SetValue(settings, string.Empty);
            settings.CommitEditBuffers();

            Assert.That(buffer.GetValue(settings), Is.EqualTo(ChronoSaveSchedule.FormatIntervalBuffer(ChronoSaveSchedule.DefaultIntervalMinutes)));
            Assert.That(settings.SaveIntervalMinutes, Is.EqualTo(ChronoSaveSchedule.DefaultIntervalMinutes));
        }

        [Test]
        public void CommitEditBuffers_KeepsAValidTypedValue()
        {
            var settings = new ChronoSaveSettings();
            var buffer = typeof(ChronoSaveSettings).GetField("saveIntervalBuffer", BindingFlags.Instance | BindingFlags.NonPublic);

            buffer.SetValue(settings, "12");
            settings.CommitEditBuffers();

            Assert.That(settings.SaveIntervalMinutes, Is.EqualTo(12f));
            Assert.That(buffer.GetValue(settings), Is.EqualTo("12"));
        }

        [Test]
        public void TheDocumentedDefaultsComeFromOnePlace()
        {
            // About.xml and the store pages quote these numbers, and they used to be repeated as
            // literals in four places in the settings class.
            var settings = new ChronoSaveSettings();

            Assert.That(settings.SaveIntervalMinutes, Is.EqualTo(ChronoSaveSchedule.DefaultIntervalMinutes));
            Assert.That(settings.NumberOfSaves, Is.EqualTo(ChronoSaveSchedule.DefaultSlots));
            Assert.That(ChronoSaveSchedule.DefaultIntervalMinutes, Is.EqualTo(5f));
            Assert.That(ChronoSaveSchedule.DefaultSlots, Is.EqualTo(10));
        }
    }
}
