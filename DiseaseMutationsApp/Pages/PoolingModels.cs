namespace DiseaseMutationsApp.Pages
{
    public enum PoolingInputMode
    {
        /// <summary>The researcher only knows how many guides they need to screen.</summary>
        GuideCount,

        /// <summary>The researcher has the actual guides: a plain list or a Builder CSV report.</summary>
        GuideList
    }
}
