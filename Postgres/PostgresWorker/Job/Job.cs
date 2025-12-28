namespace PostgresWorker.Job;

// Represents a job to be processed
// - Currently just holds an id
public record Job(
    Guid Id,
    string Payload
);
