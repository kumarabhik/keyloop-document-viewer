namespace DocViewer.Web.Api;

public sealed record DocumentSummary(int Total, int Expired, int ExpiringSoon, int Duplicates, int Conflicts);
