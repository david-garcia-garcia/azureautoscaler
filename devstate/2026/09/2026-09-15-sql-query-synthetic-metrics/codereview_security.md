# Security

1. [hard] Download integrity — `autoscaler/metrics/SqlQuerySessionFactory.cs:153` — OSS Query plans use `SSL Mode=Require` / `SslMode=Required`, which encrypt but skip CA and hostname verify; `AdoSqlQueryRowReader` then sends the Entra access token as the driver password on that session
   → Set `SSL Mode=VerifyFull` and `SslMode=VerifyFull` (Azure SQL already uses `TrustServerCertificate=False`)
   Status: done
   Argument: OSS plans use `SSL Mode=VerifyFull` / `SslMode=VerifyFull`.
