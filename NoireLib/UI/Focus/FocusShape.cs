namespace NoireLib.UI;

/// <summary>
/// The mark drawn around the control holding keyboard focus.
/// </summary>
/// <remarks>
/// All four are deliberately geometric and hard edged, because focus has to be told apart from every soft, glowing or
/// tinted mark a theme uses for hover, selection and emphasis. A focus mark that differs from those only in brightness
/// is read as "this one is selected more", which is not a thing an interface can mean.
/// </remarks>
public enum FocusShape
{
    /// <summary>
    /// A hairline outline following the whole edge. The unambiguous default, and the one that survives on a control of
    /// any size or proportion.
    /// </summary>
    Ring,

    /// <summary>
    /// A short elbow inside each corner. Quieter than <see cref="Ring"/> and lighter on a busy surface, since it marks
    /// the extent without drawing a closed shape around it.
    /// </summary>
    Corners,

    /// <summary>
    /// A matched pair of square brackets, <c>[</c> and <c>]</c>, one at each side. The most decorative of the four, and
    /// the one that needs the most room: on a narrow control the two arms crowd the content between them.
    /// </summary>
    Brackets,

    /// <summary>
    /// A bar along the bottom edge alone. The quietest, and the one that reads most naturally on a text field, where a
    /// mark on all four sides competes with the frame the field already has.
    /// </summary>
    Underline,

    /// <summary>
    /// Nothing at all. How a single widget opts out while the rest of the interface keeps its mark, by being handed a
    /// style set to this; <see cref="NoireFocus.Enabled"/> is the switch for turning it off everywhere.
    /// </summary>
    /// <remarks>
    /// Last rather than first, so the value a <see cref="FocusStyle"/> falls back to is a mark that can be seen. This
    /// is what says where the keyboard is, and an accessibility affordance whose default is "invisible" fails quietly
    /// for the people who need it.
    /// </remarks>
    None,
}
