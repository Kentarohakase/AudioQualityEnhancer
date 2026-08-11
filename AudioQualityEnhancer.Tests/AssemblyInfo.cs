// LocalizationService.Instance.Culture is process wide state, and its setter also writes
// CultureInfo.DefaultThreadCurrentUICulture. Several test classes change it while others
// assert on localized text, so running classes in parallel makes those assertions depend
// on timing. The whole suite takes a few seconds, so serializing it is the cheap fix.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
