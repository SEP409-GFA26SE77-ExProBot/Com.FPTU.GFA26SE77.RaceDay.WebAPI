namespace RaceDay.Modules.Photo.Domain;

public enum PhotoStatus
{
    Pending = 0,
    Queued = 1,
    Processing = 2,
    Matched = 3,
    NeedsReview = 4,
    ManualCorrected = 5,
    Discarded = 6,
    Failed = 7,
    RunnerReported = 8
}

public enum PhotoTagSource
{
    Ocr = 0,
    Manual = 1
}

public enum PhotoDisputeStatus
{
    Open = 0,
    Approved = 1,
    Rejected = 2
}
