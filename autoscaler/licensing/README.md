# Licensing System

This project uses an asymmetric RSA signature-based licensing system to protect prebuilt Docker images while keeping the source code open.

## How It Works

- The **public key** is embedded in the repository (anyone can verify licenses)
- The **private key** is kept secret (only the maintainer can sign licenses)
- Licenses are provided as environment variables
- If no license is provided, the application runs with an expired license (limited functionality)

## License Format

A license is a signed JWT (JSON Web Token) provided as a single environment variable:

- **AUTOSCALER_LICENSE**: JWT token containing the license claims and signature

### License JWT Claims

The JWT contains the following claims:
- `licensedTo`: Company name or individual (string)
- `exp`: Expiration date as Unix timestamp (number)
- `maxResources`: Maximum number of Azure resources allowed (number)

## Generating RSA Key Pair

To generate a new RSA key pair for signing licenses:

```bash
# Generate private key (2048 bits)
openssl genrsa -out private_key.pem 2048

# Extract public key
openssl rsa -in private_key.pem -pubout -out public_key.pem
```

**Important**: 
- Keep `private_key.pem` **SECRET** - never commit it to the repository
- Replace `autoscaler/licensing/public_key.pem` with your generated public key
- The public key is embedded in the application as a resource

## Creating a License JWT

### Using the Built-in License Generator

The easiest way is to use the built-in license generator command:

```bash
# After building the project
dotnet run -- generate-license private_key.pem "Acme Corporation" "2025-12-31T23:59:59Z" 10
```

Or if you have the compiled executable:

```bash
poolautoscaler generate-license private_key.pem "Acme Corporation" "2025-12-31T23:59:59Z" 10
```

This will output the JWT token and instructions for setting the environment variable.

### Alternative: Programmatic Generation

If you need to integrate license generation into your own service, here's an example using .NET:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

// Load private key
using var rsa = RSA.Create();
rsa.ImportFromPem(File.ReadAllText("private_key.pem"));
var signingKey = new RsaSecurityKey(rsa);

// Create claims
var expirationDate = DateTimeOffset.Parse("2025-12-31T23:59:59Z");
var claims = new[]
{
    new Claim("licensedTo", "Test Company"),
    new Claim("exp", expirationDate.ToUnixTimeSeconds().ToString()),
    new Claim("maxResources", "10")
};

// Create JWT
var tokenHandler = new JwtSecurityTokenHandler();
var tokenDescriptor = new SecurityTokenDescriptor
{
    Subject = new ClaimsIdentity(claims),
    SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
};

var token = tokenHandler.CreateToken(tokenDescriptor);
var jwt = tokenHandler.WriteToken(token);

Console.WriteLine(jwt);
```

Then set the environment variable:
```bash
export AUTOSCALER_LICENSE='<jwt-token-here>'
```

### Alternative: Using Node.js

```javascript
const jwt = require('jsonwebtoken');
const fs = require('fs');

const privateKey = fs.readFileSync('private_key.pem', 'utf8');
const expirationDate = Math.floor(new Date('2025-12-31T23:59:59Z').getTime() / 1000);

const token = jwt.sign(
  {
    licensedTo: 'Test Company',
    exp: expirationDate,
    maxResources: 10
  },
  privateKey,
  { algorithm: 'RS256' }
);

console.log(token);
```

## Expired License Behavior

When a license is expired or not provided, the application:

1. **Runtime Error**: Throws an error after 12 hours of runtime (causing container restart)
2. **Scaling Delays**: Adds 1 minute delay to all scaling operations
3. **Resource Limit**: Only processes 1 resource (even if more are configured)
4. **Trace Logging Only**: Forces trace-level logging (verbose output)

## License Information Display

On startup, the application displays license information to the console:

```
========================================
Azure Autoscaler License Information
========================================
Licensed To: Test Company
Expiration Date: 2025-12-31 23:59:59 UTC
Max Resources: 10
License Valid: Yes
License Expired: No
========================================
```

## Implementation Notes

- The license validation happens once at startup
- License status is checked throughout the application lifecycle
- The system gracefully degrades with expired licenses (doesn't crash)
- Anyone can fork the repository and remove the license checks to build their own images
