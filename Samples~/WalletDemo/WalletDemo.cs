using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using Blockmaker;

/// <summary>
/// Sample scene script — sets up the full wallet system at runtime
/// with wallet connection and transaction signing demos.
///
/// Just add this to an empty GameObject and hit Play.
/// </summary>
public class WalletDemo : MonoBehaviour
{
    private UIDocument _demoDoc;
    private VisualElement _demoPanel;
    private Label _statusLabel;
    private Button _signBtn;
    private Button _groupSignBtn;
    private PanelSettings _panelSettings;

    private void Start()
    {
        // ── 1. Config ──────────────────────────────────────────────────────
        var config = ScriptableObject.CreateInstance<BlockmakerConfig>();

        // ── 2. BlockmakerAuth ──────────────────────────────────────────────
        var authGo = new GameObject("_Blockmaker");
        DontDestroyOnLoad(authGo);
        var auth = authGo.AddComponent<BlockmakerAuth>();
        auth.blockmakerConfig = config;

        // ── 3. Auth UI (the wallet selection modal) ────────────────────────
        var authUiGo = new GameObject("_AuthUI");
        DontDestroyOnLoad(authUiGo);

        var uiDoc = authUiGo.AddComponent<UIDocument>();
        uiDoc.sortingOrder = 100;

        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        uiDoc.panelSettings = _panelSettings;

        var authPromptAsset = LoadUxml("UI/AuthPrompt.uxml");
        if (authPromptAsset != null)
            uiDoc.visualTreeAsset = authPromptAsset;

        var authPrompt = authUiGo.AddComponent<AuthPromptController>();
        authPrompt.peraConnectAsset = LoadUxml("UI/PeraConnectModal.uxml");

        // ── 4. Connect button ──────────────────────────────────────────────
        var btnGo = new GameObject("_ConnectButton");
        DontDestroyOnLoad(btnGo);
        var btnDoc = btnGo.AddComponent<UIDocument>();
        btnDoc.sortingOrder = 200;
        btnDoc.panelSettings = _panelSettings;
        btnGo.AddComponent<BlockmakerConnectButton>();

        // ── 5. Profile UI ─────────────────────────────────────────────────
        var profileGo = new GameObject("_ProfileUI");
        DontDestroyOnLoad(profileGo);
        var profileDoc = profileGo.AddComponent<UIDocument>();
        profileDoc.sortingOrder = 150;
        profileDoc.panelSettings = _panelSettings;
        var profileUxml = LoadUxml("UI/BlockmakerProfile.uxml");
        if (profileUxml != null)
            profileDoc.visualTreeAsset = profileUxml;
        profileGo.AddComponent<BlockmakerProfileController>();

        // ── 6. Profile button ─────────────────────────────────────────────
        var profileBtnGo = new GameObject("_ProfileButton");
        DontDestroyOnLoad(profileBtnGo);
        var profileBtnDoc = profileBtnGo.AddComponent<UIDocument>();
        profileBtnDoc.sortingOrder = 210;
        profileBtnDoc.panelSettings = _panelSettings;
        profileBtnGo.AddComponent<BlockmakerProfileButton>();

        // ── 7. Demo panel (sign transaction buttons) ───────────────────────
        var demoGo = new GameObject("_DemoPanel");
        DontDestroyOnLoad(demoGo);
        _demoDoc = demoGo.AddComponent<UIDocument>();
        _demoDoc.sortingOrder = 50;
        _demoDoc.panelSettings = _panelSettings;

        BuildDemoPanel();

        // ── 6. Listen for wallet connection ─────────────────────────────────
        BlockmakerAuth.OnIdentityChanged += HandleIdentityChanged;

        Debug.Log("[WalletDemo] Wallet system ready. Click 'Connect Wallet' to start.");
    }

    private void OnDestroy()
    {
        BlockmakerAuth.OnIdentityChanged -= HandleIdentityChanged;
    }

    private static VisualTreeAsset LoadUxml(string path)
    {
        #if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"Packages/com.blockmaker.sdk/{path}");
        #else
        return Resources.Load<VisualTreeAsset>(System.IO.Path.GetFileNameWithoutExtension(path));
        #endif
    }

    private void BuildDemoPanel()
    {
        var root = _demoDoc.rootVisualElement;
        root.Clear();

        _demoPanel = new VisualElement();
        _demoPanel.style.position = Position.Absolute;
        _demoPanel.style.bottom = 24;
        _demoPanel.style.left = 24;
        _demoPanel.style.backgroundColor = new Color(0.11f, 0.11f, 0.14f, 0.95f);
        _demoPanel.style.borderTopLeftRadius = 12;
        _demoPanel.style.borderTopRightRadius = 12;
        _demoPanel.style.borderBottomLeftRadius = 12;
        _demoPanel.style.borderBottomRightRadius = 12;
        _demoPanel.style.borderTopWidth = 1;
        _demoPanel.style.borderBottomWidth = 1;
        _demoPanel.style.borderLeftWidth = 1;
        _demoPanel.style.borderRightWidth = 1;
        _demoPanel.style.borderTopColor = new Color(1, 1, 1, 0.08f);
        _demoPanel.style.borderBottomColor = new Color(0, 0, 0, 0.3f);
        _demoPanel.style.borderLeftColor = new Color(1, 1, 1, 0.05f);
        _demoPanel.style.borderRightColor = new Color(1, 1, 1, 0.05f);
        _demoPanel.style.paddingLeft = 20;
        _demoPanel.style.paddingRight = 20;
        _demoPanel.style.paddingTop = 16;
        _demoPanel.style.paddingBottom = 16;
        _demoPanel.style.width = 320;
        _demoPanel.style.display = DisplayStyle.None;

        var title = new Label("Transaction Signing Demo");
        title.style.fontSize = 14;
        title.style.color = new Color(0.94f, 0.94f, 0.96f);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 12;
        _demoPanel.Add(title);

        _signBtn = CreateButton("Sign Transaction (0 ALGO to self)", () => StartCoroutine(SignSingleTransaction()));
        _demoPanel.Add(_signBtn);

        _groupSignBtn = CreateButton("Sign Atomic Group (2 txns)", () => StartCoroutine(SignGroupTransaction()));
        _groupSignBtn.style.marginTop = 8;
        _demoPanel.Add(_groupSignBtn);

        _statusLabel = new Label("");
        _statusLabel.style.fontSize = 12;
        _statusLabel.style.color = new Color(0.53f, 0.53f, 0.53f);
        _statusLabel.style.marginTop = 12;
        _statusLabel.style.whiteSpace = WhiteSpace.Normal;
        _demoPanel.Add(_statusLabel);

        root.Add(_demoPanel);
    }

    private Button CreateButton(string text, System.Action onClick)
    {
        var btn = new Button();
        btn.text = text;
        btn.style.paddingLeft = 16;
        btn.style.paddingRight = 16;
        btn.style.paddingTop = 10;
        btn.style.paddingBottom = 10;
        btn.style.fontSize = 12;
        btn.style.color = new Color(0.94f, 0.94f, 0.96f);
        btn.style.backgroundColor = new Color(1, 1, 1, 0.06f);
        btn.style.borderTopWidth = 1;
        btn.style.borderBottomWidth = 1;
        btn.style.borderLeftWidth = 1;
        btn.style.borderRightWidth = 1;
        btn.style.borderTopColor = new Color(1, 1, 1, 0.08f);
        btn.style.borderBottomColor = new Color(1, 1, 1, 0.03f);
        btn.style.borderLeftColor = new Color(1, 1, 1, 0.06f);
        btn.style.borderRightColor = new Color(1, 1, 1, 0.06f);
        btn.style.borderTopLeftRadius = 8;
        btn.style.borderTopRightRadius = 8;
        btn.style.borderBottomLeftRadius = 8;
        btn.style.borderBottomRightRadius = 8;
        btn.clicked += onClick;
        return btn;
    }

    private void HandleIdentityChanged(IBlockmakerIdentity identity)
    {
        _demoPanel.style.display = identity.HasWallet ? DisplayStyle.Flex : DisplayStyle.None;
        _statusLabel.text = "";

        if (identity.HasWallet)
            Debug.Log($"[WalletDemo] Connected: {identity.ProviderName} — {identity.Address}");
        else
            Debug.Log("[WalletDemo] Disconnected — guest mode");
    }

    private void SetStatus(string text, bool isError = false)
    {
        _statusLabel.text = text;
        _statusLabel.style.color = isError
            ? new Color(0.94f, 0.26f, 0.26f)
            : new Color(0.063f, 0.725f, 0.506f);
    }

    private IEnumerator SignSingleTransaction()
    {
        var identity = BlockmakerAuth.Instance?.Identity;
        if (identity == null || !identity.CanSign)
        {
            SetStatus("Wallet cannot sign right now.", true);
            yield break;
        }

        if (BlockmakerClient.Instance == null)
        {
            SetStatus("Server not connected.", true);
            yield break;
        }

        SetStatus("Building transaction...");
        _signBtn.SetEnabled(false);

        string unsignedTxn = null;
        string buildError = null;
        bool buildDone = false;

        BlockmakerClient.Instance.BuildPayment(
            identity.Address, 0, "Blockmaker SDK — test transaction",
            result => { unsignedTxn = result.unsignedTxnBase64; buildDone = true; },
            err => { buildError = err; buildDone = true; }
        );

        float elapsed = 0f;
        while (!buildDone && elapsed < 15f) { elapsed += Time.unscaledDeltaTime; yield return null; }

        if (!buildDone || buildError != null)
        {
            SetStatus(buildError ?? "Timed out building transaction.", true);
            _signBtn.SetEnabled(true);
            yield break;
        }

        SetStatus("Approve in your wallet app...");

        string signedTxn = null;
        string signError = null;

        yield return identity.SignTransaction(unsignedTxn,
            s => { signedTxn = s; },
            e => { signError = e; }
        );

        _signBtn.SetEnabled(true);

        if (signedTxn != null)
        {
            SetStatus($"Signed! ({signedTxn.Length} chars)");
            Debug.Log($"[WalletDemo] Transaction signed successfully. Length: {signedTxn.Length}");
        }
        else
        {
            SetStatus(signError ?? "Signing failed.", true);
        }
    }

    private IEnumerator SignGroupTransaction()
    {
        var identity = BlockmakerAuth.Instance?.Identity;
        if (identity == null || !identity.CanSign)
        {
            SetStatus("Wallet cannot sign right now.", true);
            yield break;
        }

        if (BlockmakerClient.Instance == null)
        {
            SetStatus("Server not connected.", true);
            yield break;
        }

        SetStatus("Building 2 transactions...");
        _groupSignBtn.SetEnabled(false);

        string txn1 = null, txn2 = null;
        string buildError = null;
        int completed = 0;

        BlockmakerClient.Instance.BuildPayment(
            identity.Address, 0, "Blockmaker SDK — group txn 1",
            result => { txn1 = result.unsignedTxnBase64; completed++; },
            err => { buildError = err; completed++; }
        );

        BlockmakerClient.Instance.BuildPayment(
            identity.Address, 0, "Blockmaker SDK — group txn 2",
            result => { txn2 = result.unsignedTxnBase64; completed++; },
            err => { buildError = err; completed++; }
        );

        float elapsed = 0f;
        while (completed < 2 && elapsed < 15f) { elapsed += Time.unscaledDeltaTime; yield return null; }

        if (buildError != null || txn1 == null || txn2 == null)
        {
            SetStatus(buildError ?? "Timed out building transactions.", true);
            _groupSignBtn.SetEnabled(true);
            yield break;
        }

        SetStatus("Approve group in your wallet app...");

        string[] signedTxns = null;
        string signError = null;

        yield return identity.SignTransactions(new[] { txn1, txn2 },
            s => { signedTxns = s; },
            e => { signError = e; }
        );

        _groupSignBtn.SetEnabled(true);

        if (signedTxns != null)
        {
            SetStatus($"Group signed! ({signedTxns.Length} txns)");
            Debug.Log($"[WalletDemo] Atomic group signed. {signedTxns.Length} transactions.");
        }
        else
        {
            SetStatus(signError ?? "Group signing failed.", true);
        }
    }
}
