using System;
using System.Collections;
using UnityEngine.Networking;
using Blockmaker;
using UnityEngine;
using UnityEngine.UIElements;

// Attach beside a UIDocument. This sample creates no transactions and logs no
// tokens, provider user data, keys or signed bytes. Configure each game's own ID.
public sealed class BlockmakerEmailSample : MonoBehaviour
{
    public UIDocument Document;
    public string ApiOrigin = "https://blockmaker.polaris.city";
    public string GameId;
    public string PublicEmailClientId; // blank keeps Pera/Lute
    public string AppName = "Wallet sample";
    public Font Font;
    private BlockmakerClient client;
    private BlockmakerWalletPackageWebGL package;
    private BlockmakerUnityWalletView view;
    private Label status;
    private Button connect, logout, profile;
    private Image picture;
    private Texture2D pictureTexture;
    private int profileRead;

    private void Start()
    {
        gameObject.name = "Blockmaker email sample receiver";
        var root = Document.rootVisualElement;
        status = new Label("Preparing wallets…"); root.Add(status);
        picture = new Image(); picture.style.width = picture.style.height = 128; root.Add(picture);
        connect = new Button(Open) { text = "Sign in" }; root.Add(connect); connect.SetEnabled(false);
        logout = new Button(() => package.Logout(result => {
            if (result.Success) { profileRead++; ClearPicture(); }
            status.text = result.Success ? "Signed out" : result.Message;
            connect.SetEnabled(result.Success); profile.SetEnabled(!result.Success);
        })) { text = "Sign out" }; root.Add(logout);
        profile = new Button(LoadProfile) { text = "Refresh shared profile" }; root.Add(profile); profile.SetEnabled(false);
        client = new BlockmakerClient(ApiOrigin, GameId);
        package = gameObject.AddComponent<BlockmakerWalletPackageWebGL>();
        if (!BlockmakerWalletPackageWebGL.RuntimeSupported) { status.text = "Run this sample as Unity WebGL."; return; }
        package.Initialize(client, PublicEmailClientId, AppName, result => {
            status.text = result.Success ? "Choose a wallet to continue" : result.Message;
            connect.SetEnabled(result.Success);
        });
    }
    private void Open()
    {
        view?.Dispose();
        view = new BlockmakerUnityWalletView(Document.rootVisualElement, package,
            new BlockmakerUnityWalletAppearance { AppName = AppName, Font = Font });
        view.OpenAccount(result => {
            view?.Dispose(); view = null;
            bool ready = result.Success && package.HasAcknowledgedPlayerSession && package.CanSignTransactions;
            connect.SetEnabled(!ready); profile.SetEnabled(ready);
            status.text = ready ? client.WalletAddress + "\nAlgorand MainNet. Email login does not fund this address." : result.Message;
            if (ready) LoadProfile();
        });
    }
    private void LoadProfile()
    {
        if (!package.HasAcknowledgedPlayerSession) return;
        var wallet = client.WalletAddress;
        int read = ++profileRead;
        StartCoroutine(client.GetGameProfile(response => {
            if (read != profileRead || !package.HasAcknowledgedPlayerSession || client.WalletAddress != wallet) return;
            if (!response.Success) { status.text = response.Error; return; }
            var shared = response.Parse<BlockmakerGameProfileResponse>()?.profile;
            if (shared == null || shared.walletAddress != wallet) return;
            status.text = shared.displayName + "\n" + wallet;
            ClearPicture();
            if (!string.IsNullOrEmpty(shared.profileImageUrl)) StartCoroutine(LoadPicture(shared.profileImageUrl, wallet, read));
        }));
    }
    private IEnumerator LoadPicture(string source, string wallet, int read)
    {
        if (source.StartsWith("/", StringComparison.Ordinal)) source = ApiOrigin.TrimEnd('/') + source;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https") yield break;
        using (var request = UnityWebRequestTexture.GetTexture(source)) {
            request.timeout = 15;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            var texture = DownloadHandlerTexture.GetContent(request);
            if (read != profileRead || !package.HasAcknowledgedPlayerSession || client.WalletAddress != wallet) { Destroy(texture); yield break; }
            ClearPicture(); pictureTexture = texture; picture.image = texture;
        }
    }
    private void ClearPicture() { if (picture != null) picture.image = null; if (pictureTexture != null) Destroy(pictureTexture); pictureTexture = null; }
    private void OnDestroy() { profileRead++; view?.Dispose(); ClearPicture(); if (package != null) package.Cancel(); }
}
