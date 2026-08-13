# TestSite

A stock Umbraco with `BaryoDev.Umbraco.Pwa` added and nothing else, so anything that works here
works because of the package rather than the site around it.

It is used two ways: the integration tests boot it in-process through `WebApplicationFactory`, and
the same project is built into the public demo container.

## Credentials

`appsettings.json` carries an obvious placeholder password for local use only. **The public demo
overrides it from the environment**, so no working credential is ever committed:

```sh
docker run -e Umbraco__CMS__Unattended__UnattendedUserPassword="..." ...
```

Anything committed here should be assumed public, because it is.

## Secrets

Two values in `appsettings.json` are deliberately blank or placeholders, and both are supplied from
the environment when the demo is deployed:

| Key | Why it matters |
| --- | --- |
| `Umbraco:CMS:Unattended:UnattendedUserPassword` | The backoffice login |
| `Umbraco:CMS:Imaging:HMACSecretKey` | Signs image-processing URLs. With it, anyone can craft arbitrary resize requests against the site |

The Umbraco project template generates a real `HMACSecretKey` straight into `appsettings.json`, so
it lands in the first commit unless you look for it. That is worth knowing for any Umbraco repo,
not just this one.
