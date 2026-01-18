# SSL Certificate Setup for Production Deployment

## Overview

This document provides instructions for configuring SSL/HTTPS certificates for the Travel Agency Service application in production environments. The application already includes HTTPS redirection in the code (`app.UseHttpsRedirection()` in `Program.cs`), which is required for secure payment processing.

## Current Implementation

The application is already configured to:
- ✅ Redirect HTTP traffic to HTTPS (`app.UseHttpsRedirection()` in `Program.cs`)
- ✅ Use HTTPS in development (development certificate)
- ✅ Enforce HTTPS in production (`app.UseHsts()` when not in development)

## Production SSL Certificate Options

### Option 1: Let's Encrypt (Recommended - Free)

Let's Encrypt provides free, automated SSL certificates that are trusted by all major browsers.

#### For Linux/Ubuntu Server:

1. **Install Certbot:**
   ```bash
   sudo apt-get update
   sudo apt-get install certbot python3-certbot-nginx
   ```

2. **Obtain Certificate:**
   ```bash
   sudo certbot --nginx -d yourdomain.com -d www.yourdomain.com
   ```

3. **Auto-renewal (automatic):**
   Certbot sets up automatic renewal. Test renewal with:
   ```bash
   sudo certbot renew --dry-run
   ```

#### For Windows Server with IIS:

1. **Install Win-ACME (Windows ACME Simple):**
   - Download from: https://www.win-acme.com/
   - Run the installer

2. **Run Win-ACME:**
   ```powershell
   wacs.exe
   ```
   - Select option to create a new certificate
   - Choose your IIS site
   - Follow the prompts

3. **Auto-renewal:**
   Win-ACME sets up a scheduled task for automatic renewal

#### For Docker/Kubernetes:

Use cert-manager with Let's Encrypt:
- Install cert-manager in your cluster
- Configure Certificate and ClusterIssuer resources
- Annotations automatically request and renew certificates

### Option 2: Cloud Provider SSL Certificates

#### Azure App Service:
1. Go to App Service → SSL settings
2. Add SSL certificate (App Service Certificate or upload your own)
3. Enable HTTPS Only in Configuration → General settings
4. Bind certificate to your custom domain

#### AWS (Elastic Beanstalk/EC2):
1. Request certificate via AWS Certificate Manager (ACM)
2. For Application Load Balancer: Configure HTTPS listener with ACM certificate
3. For EC2: Use Let's Encrypt or upload certificate to EC2 instance

#### Google Cloud Platform:
1. Use Google-managed SSL certificates (recommended for App Engine/Load Balancer)
2. Or upload your own certificate in Cloud Console

### Option 3: Commercial SSL Certificates

Purchase SSL certificates from providers like:
- DigiCert
- GlobalSign
- Comodo/Sectigo
- GoDaddy SSL

Then configure them according to your hosting provider's instructions.

## Configuration for ASP.NET Core

### 1. Production Configuration

Ensure your `appsettings.Production.json` includes:

```json
{
  "Kestrel": {
    "Endpoints": {
      "HttpsInlineCertAndKeyFile": {
        "Url": "https://localhost:5001",
        "Certificate": {
          "Path": "/path/to/certificate.pfx",
          "Password": "certificate-password"
        }
      }
    }
  }
}
```

### 2. Environment-Specific Settings

For different environments, use:
- `appsettings.Development.json` - Development certificates (auto-generated)
- `appsettings.Production.json` - Production SSL configuration
- Environment variables for sensitive certificate passwords

### 3. Kestrel Configuration (if using Kestrel directly)

In `Program.cs` or `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:443",
        "Certificate": {
          "Path": "/etc/ssl/certs/certificate.pfx",
          "Password": "${CERT_PASSWORD}"
        }
      }
    }
  }
}
```

## Reverse Proxy Setup

### Nginx (Linux)

1. **Configure Nginx:**
   ```nginx
   server {
       listen 80;
       server_name yourdomain.com www.yourdomain.com;
       return 301 https://$server_name$request_uri;
   }

   server {
       listen 443 ssl http2;
       server_name yourdomain.com www.yourdomain.com;

       ssl_certificate /etc/letsencrypt/live/yourdomain.com/fullchain.pem;
       ssl_certificate_key /etc/letsencrypt/live/yourdomain.com/privkey.pem;

       location / {
           proxy_pass http://localhost:5000;
           proxy_http_version 1.1;
           proxy_set_header Upgrade $http_upgrade;
           proxy_set_header Connection keep-alive;
           proxy_set_header Host $host;
           proxy_cache_bypass $http_upgrade;
           proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
           proxy_set_header X-Forwarded-Proto $scheme;
       }
   }
   ```

2. **Test and reload:**
   ```bash
   sudo nginx -t
   sudo systemctl reload nginx
   ```

### IIS (Windows Server)

1. **Import Certificate:**
   - Open IIS Manager
   - Server Certificates → Import
   - Select your .pfx file and enter password

2. **Bind Certificate:**
   - Select your site → Bindings
   - Add HTTPS binding
   - Select the imported certificate
   - Port: 443

3. **Enable HTTPS Redirect (optional):**
   - Install URL Rewrite module
   - Add redirect rule from HTTP to HTTPS

### Apache (Linux)

```apache
<VirtualHost *:443>
    ServerName yourdomain.com
    SSLEngine on
    SSLCertificateFile /etc/ssl/certs/certificate.crt
    SSLCertificateKeyFile /etc/ssl/private/certificate.key
    SSLCertificateChainFile /etc/ssl/certs/chain.crt

    ProxyPreserveHost On
    ProxyPass / http://localhost:5000/
    ProxyPassReverse / http://localhost:5000/
</VirtualHost>

<VirtualHost *:80>
    ServerName yourdomain.com
    Redirect permanent / https://yourdomain.com/
</VirtualHost>
```

## Security Best Practices

### 1. HTTP Strict Transport Security (HSTS)

Already configured in `Program.cs`:
```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseHsts(); // Enforces HTTPS for 1 year
}
```

### 2. TLS Version Configuration

For Kestrel, ensure TLS 1.2 or higher:
```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | 
                                    System.Security.Authentication.SslProtocols.Tls13;
    });
});
```

### 3. Certificate Validation

Ensure proper certificate validation:
- Never disable certificate validation in production
- Use trusted Certificate Authorities (CAs)
- Keep certificates up to date (enable auto-renewal)

### 4. Security Headers

Consider adding security headers middleware for additional protection:
- Content-Security-Policy
- X-Frame-Options
- X-Content-Type-Options

## Testing SSL Configuration

### 1. Online SSL Checkers:
- SSL Labs SSL Test: https://www.ssllabs.com/ssltest/
- SecurityHeaders.com: https://securityheaders.com/

### 2. Manual Testing:
```bash
# Check certificate details
openssl s_client -connect yourdomain.com:443 -servername yourdomain.com

# Check certificate expiration
echo | openssl s_client -connect yourdomain.com:443 -servername yourdomain.com 2>/dev/null | openssl x509 -noout -dates
```

### 3. Browser Testing:
- Navigate to your site using `https://`
- Check for padlock icon in browser
- Verify certificate details in browser
- Test HTTPS redirect from HTTP

## Troubleshooting

### Certificate Not Trusted
- Ensure certificate is from a trusted CA
- Check certificate chain is complete
- Verify certificate is for the correct domain

### Mixed Content Warnings
- Ensure all resources (CSS, JS, images) use HTTPS
- Update any hardcoded HTTP URLs to HTTPS
- Use protocol-relative URLs or HTTPS only

### Certificate Expiration
- Set up automatic renewal
- Monitor certificate expiration dates
- Test renewal process before expiration

### ASP.NET Core Not Accepting HTTPS
- Check Kestrel configuration
- Verify certificate path and permissions
- Check application logs for SSL errors
- Ensure firewall allows port 443

## Payment Processing Requirements

As per project requirements:
- ✅ SSL certificate is **mandatory** for payment processing
- ✅ Free certificates (Let's Encrypt) are allowed
- ✅ HTTPS redirection is implemented
- ✅ Payment pages must use HTTPS

## Summary

1. **Development:** Uses automatic development certificates (already configured)
2. **Production:** Choose a certificate option (Let's Encrypt recommended)
3. **Configuration:** Configure based on your hosting environment
4. **Testing:** Verify SSL is working correctly
5. **Maintenance:** Set up automatic certificate renewal

## Additional Resources

- [ASP.NET Core HTTPS Documentation](https://docs.microsoft.com/en-us/aspnet/core/security/enforcing-ssl)
- [Let's Encrypt Documentation](https://letsencrypt.org/docs/)
- [SSL Labs SSL Test](https://www.ssllabs.com/ssltest/)
- [OWASP TLS Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Transport_Layer_Protection_Cheat_Sheet.html)

---

**Note:** This application already has HTTPS redirection implemented in `Program.cs`. For production deployment, you only need to configure the SSL certificate according to your hosting environment using one of the methods described above.
