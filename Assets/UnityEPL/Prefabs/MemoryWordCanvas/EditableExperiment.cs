using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EditableExperiment : MonoBehaviour
{
    public TextDisplayer textDisplayer;
    public TextDisplayer fullscreenTextDisplayer;
    public UnityEngine.UI.InputField inputField;
    public ScriptedEventReporter scriptedEventReporter;
    public SoundRecorder soundRecorder;
    public VoiceActivityDetection voiceActivityDetection;
    public GameObject tooSoonWarning;

    public GameObject microphoneTestMessage;

    public AudioSource audioPlayback;
    public AudioSource highBeep;
    public AudioSource lowBeep;

    private string[] words;
    private Dictionary<string, int> numberingWords;

    private int numberOfLists = 16;
    private int lengthOfList = 15;

    private const string FIRST_INSTRUCTIONS_MESSAGE = 
"\n\n\nWe will now review the basics of the study, and the experimenter will answer any questions that you have.\n\n\n1) In this study, lists of words will appear on the computer screen. \n\n2) After the last word in each list, you will see a row of asterisks (*******) flash on the screen and you will hear a tone. At this time, say as many words as you can remember from the list, IN ANY ORDER.\n\n3) Speak loudly and clearly. You will have a fixed amount of time in which to recall the list. Please try hard throughout the recall period, as you may recall some words even when you feel you have exhausted your memory. \n\n\n";
    private const string SECOND_INSTRUCTIONS_MESSAGE =
        "\n\n\n4) It is very important for you to avoid all unnecessary motion while engaged in the study. \n\n5) Please try to avoid blinking while each word remains on the screen. \n\n\nYou are now ready to begin the study! \n\nIf you have any remaining questions, please ask the experimenter now. Otherwise, press RETURN to continue.\n\n\n";
    private const string BREAK_MESSAGE =
"\n\n\nWe will now take some time\nto readjust the electrodes.\nWhen it is time to continue,\npress SPACE and RETURN.\n\n\n";
    private const string EXPERIMENTER_MESSAGE =
"Researcher: Please confirm that the impedance window is closed and that sync pulses are showing.";
    
    void Start()
    {
        UnityEPL.SetExperimentName("prelim");
        LoadWords();
        LoadNumberingPool();
        StartCoroutine(RunExperiment());
    }

    private void LoadNumberingPool()
    {
        numberingWords = new Dictionary<string, int>();
        string[] numberingPool = GetWordpoolLines("ram_wordpool_en", false);
        for (int i = 1; i <= numberingPool.Length; i++)
        {
            numberingWords.Add(numberingPool[i - 1], i);
        }
    }

    private int GetWordNumber(string word)
    {
        if (word[word.Length - 1] == ' ')
            word = word.Substring(0, word.Length - 1);
        if (numberingWords.ContainsKey(word))
        {
            Debug.Log(numberingWords[word]);
            return numberingWords[word];
        }
        else
        {
            Debug.Log("-1");
            return -1;
        }
    }

    private void LoadWords()
    {
        words = GetWordpoolLines("ram_wordpool_en", true);
    }

    private IEnumerator RunExperiment()
    {
        textDisplayer.DisplayText("subject name prompt", "Please enter the subject name and then press enter.");
        yield return new WaitForSeconds(3f);
        textDisplayer.ClearText();
        inputField.gameObject.SetActive(true);
        inputField.Select();
        do
        {
            yield return null;
            while (!Input.GetKeyDown(KeyCode.Return))
                yield return null;
        }
        while (!inputField.text.Equals("TEST") && (inputField.text.Length != 7 || 
                                                   !inputField.text[0].Equals('P') || 
                                                   !inputField.text[1].Equals('L') || 
                                                   !inputField.text[2].Equals('T') || 
                                                   !inputField.text[3].Equals('P')));
        UnityEPL.AddParticipant(inputField.text);
        SetSessionNumber();
        inputField.gameObject.SetActive(false);
        Cursor.visible = false;

        //Add part here which calls eeg file checking script
        yield return PressAnyKey(UnityEPL.GetParticipants()[0] + "\nsession " + UnityEPL.GetSessionNumber(), new KeyCode[] { KeyCode.Return }, textDisplayer);
        yield return PressAnyKey("Researcher:\nPlease confirm that the \nimpedance window is closed\nand that sync pulses are showing", new KeyCode[] { KeyCode.Y }, textDisplayer);
        yield return PressAnyKey("Researcher:\nPlease begin the EEG recording now\nand confirm that it is running.", new KeyCode[] { KeyCode.R }, textDisplayer);

        yield return EEGVerificationScript(UnityEPL.GetExperimentName(), UnityEPL.GetParticipants()[0], UnityEPL.GetSessionNumber());

        scriptedEventReporter.ReportScriptedEvent("microphone test begin", new Dictionary<string, object>());
        yield return DoMicrophoneTest();
        scriptedEventReporter.ReportScriptedEvent("microphone test end", new Dictionary<string, object>());

        fullscreenTextDisplayer.textElements[0].alignment = TextAnchor.MiddleLeft;
        yield return PressAnyKey(FIRST_INSTRUCTIONS_MESSAGE, new KeyCode[] { KeyCode.Return }, fullscreenTextDisplayer);
        yield return PressAnyKey(SECOND_INSTRUCTIONS_MESSAGE, new KeyCode[] { KeyCode.Return }, fullscreenTextDisplayer);
        fullscreenTextDisplayer.textElements[0].alignment = TextAnchor.MiddleCenter;

        // Begin free recall trials
        for (int i=0; i < numberOfLists; i++)
        {
            if (i!=0) 
            {
                yield return PressAnyKey("Press SPACE to continue.", new KeyCode[] { KeyCode.Space }, textDisplayer);
            }
            textDisplayer.DisplayText("list count", "List " + (i+1));
            yield return new WaitForSeconds(3f);
            textDisplayer.ClearText();
            yield return DoCountdown();
            string[] trialWords = new string[lengthOfList];
            System.Array.Copy(words, i*lengthOfList, trialWords, 0, lengthOfList);
            Debug.Log(trialWords);
            yield return PerformTrial(trialWords, i, false);
        }

        //over
        textDisplayer.DisplayText("end message", "Yay, the session is over!");
    }

    private IEnumerator DoCountdown()
    {
        for (int i = 5; i > 0; i--)
        {
            textDisplayer.DisplayText("countdown", i.ToString());
            yield return new WaitForSeconds(1);
            textDisplayer.ClearText();
        }
    }

    private IEnumerator EEGVerificationScript(string experiment, string participant, int session)
    {
        Debug.Log("eeg verification");
        while (!System.IO.File.Exists("/Users/exp/bin/check_eegfile.py"))
        {
            yield return PressAnyKey("I couldn't find /User/exp/bin/check_eegfile.py .  Please make sure it exists and then press RETURN to try again.", new KeyCode[] { KeyCode.Return }, textDisplayer);
        }

        System.Diagnostics.ProcessStartInfo processStart = new System.Diagnostics.ProcessStartInfo();
        processStart.UseShellExecute = true;
        processStart.FileName = "python";
        processStart.Arguments = "/Users/exp/bin/check_eegfile.py " + experiment + " " + participant + " " + session.ToString();

        using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(processStart))
        {
            while (!process.HasExited)
            {
                yield return null;
            }

            Debug.Log("exit code: " + process.ExitCode);
            if (!(process.ExitCode == 0))
            {
                textDisplayer.DisplayText("skippable script", "check_eegfile.py indicated that the eeg file doesn't exist.  Press RETURN to try again.");
                yield return null;
                while (true)
                {
                    yield return null;
                    if (Input.GetKeyDown(KeyCode.Return))
                        break;
                    if (Input.GetKeyDown(KeyCode.S))
                        yield break;
                }
                textDisplayer.ClearText();
                yield return EEGVerificationScript(experiment, participant, session);
            }
            else
            {
                yield return PressAnyKey("Successfully verified existence of eeg file.  Press RETURN to continue.", new KeyCode[] { KeyCode.Return }, textDisplayer);
            }
        }
    }

    protected IEnumerator DoMicrophoneTest()
    {
        microphoneTestMessage.SetActive(true);
        bool repeat = false;
        string wavFilePath;

        do
        {
            yield return PressAnyKey("Press the spacebar to record a sound after the beep.", new KeyCode[]{KeyCode.Space}, textDisplayer);
            lowBeep.Play();
            textDisplayer.DisplayText("microphone test recording", "Recording...");
            textDisplayer.ChangeColor(Color.red);
            yield return new WaitForSeconds(lowBeep.clip.length);

            wavFilePath = System.IO.Path.Combine(UnityEPL.GetDataPath(), "microphone_test_" + DataReporter.RealWorldTime().ToString("yyyy-MM-dd_HH_mm_ss") + ".wav");
            soundRecorder.StartRecording();
            yield return new WaitForSeconds(5f);

            soundRecorder.StopRecording(wavFilePath);
            textDisplayer.ClearText();

            yield return new WaitForSeconds(1f);

            textDisplayer.DisplayText("microphone test playing", "Playing...");
            textDisplayer.ChangeColor(Color.green);
            audioPlayback.clip = soundRecorder.AudioClipFromDatapath(wavFilePath);
            audioPlayback.Play();
            yield return new WaitForSeconds(5f);
            textDisplayer.ClearText();
            textDisplayer.OriginalColor();

            textDisplayer.DisplayText("microphone test confirmation", "Did you hear the recording? \n(Y=Continue / N=Try Again / C=Cancel).");
            while (!Input.GetKeyDown(KeyCode.Y) && !Input.GetKeyDown(KeyCode.N) && !Input.GetKeyDown(KeyCode.C))
            {
                yield return null;
            }
            textDisplayer.ClearText();

            if (Input.GetKey(KeyCode.C))
                Quit();
            repeat = Input.GetKey(KeyCode.N);
        }
        while (repeat);

        if (!System.IO.File.Exists(wavFilePath))
            yield return PressAnyKey("WARNING: Wav output file not detected.  Sounds may not be successfully recorded to disk.", new KeyCode[] { KeyCode.Return }, textDisplayer);

        microphoneTestMessage.SetActive(false);
    }

    protected IEnumerator PressAnyKey(string displayText, KeyCode[] keyCodes, TextDisplayer pressAnyTextDisplayer)
    {
        yield return null;
        pressAnyTextDisplayer.DisplayText("press any key prompt", displayText);
        Dictionary<KeyCode, bool> keysPressed = new Dictionary<KeyCode, bool>();
        foreach (KeyCode keycode in keyCodes)
            keysPressed.Add(keycode, false);
        while (true)
        {
            yield return null;
            foreach (KeyCode keyCode in keyCodes)
            {
                if (Input.GetKeyDown(keyCode))
                    keysPressed[keyCode] = true;
                if (Input.GetKeyUp(keyCode))
                    keysPressed[keyCode] = false;
            }
            bool done = true;
            foreach (bool pressed in keysPressed.Values)
            {
                if (!pressed)
                    done = false;
            }
            if (done)
                break;
        }
        pressAnyTextDisplayer.ClearText();
    }

    private IEnumerator PerformTrial(string[] trial_words, int list_index, bool practice)
    {
        float ISI_MIN = 0.8f;
        float ISI_MAX = 1.2f;
        float STIMULUS_DISPLAY_LENGTH = 1.6f;
        float RECALL_LENGTH = 60f;

        //first isi
        yield return new WaitForSeconds(Random.Range(ISI_MIN, ISI_MAX));

        //stimulus list
        for (int sp=0; sp < lengthOfList; sp++)
        {
            string stimulus = trial_words[sp];
            Debug.Log(stimulus);
            scriptedEventReporter.ReportScriptedEvent("stimulus", new Dictionary<string, object> () { {"word", stimulus}, { "index", list_index}, {"ltp word number", GetWordNumber(stimulus)} });
            textDisplayer.DisplayText("stimulus display", stimulus);
            yield return new WaitForSeconds(STIMULUS_DISPLAY_LENGTH);
            scriptedEventReporter.ReportScriptedEvent("stimulus cleared", new Dictionary<string, object>() { { "word", stimulus }, { "index", list_index } });
            textDisplayer.ClearText();
            //isi
            yield return new WaitForSeconds(Random.Range(ISI_MIN, ISI_MAX));
        }

        //recall
        soundRecorder.StartRecording();
        scriptedEventReporter.ReportScriptedEvent("recall start", new Dictionary<string, object>());
        textDisplayer.DisplayText("display recall text", "*******");

        //begin of recall beep
        scriptedEventReporter.ReportScriptedEvent("begin beep start", new Dictionary<string, object>());
        lowBeep.Play();
        yield return new WaitForSeconds(lowBeep.clip.length);
        scriptedEventReporter.ReportScriptedEvent("begin beep stop", new Dictionary<string, object>());

        yield return new WaitForSeconds(RECALL_LENGTH);
        textDisplayer.ClearText();
        scriptedEventReporter.ReportScriptedEvent("recall stop", new Dictionary<string, object>());

        //stop recording and write .wav
        string wav_path = System.IO.Path.Combine(UnityEPL.GetDataPath(), list_index.ToString() + ".wav");
        soundRecorder.StopRecording(wav_path);

        //write .lst
        string lst_path = System.IO.Path.Combine(UnityEPL.GetDataPath(), list_index.ToString() + ".lst");
        WriteAllLinesNoExtraNewline(lst_path, trial_words);

        //end of recall beep
        scriptedEventReporter.ReportScriptedEvent("end beep start", new Dictionary<string, object>());
        highBeep.Play();
        yield return new WaitForSeconds(highBeep.clip.length);
        scriptedEventReporter.ReportScriptedEvent("end beep stop", new Dictionary<string, object>());

    }

    private string[] GetWordpoolLines(string path, bool shuffle)
    {
        string text = Resources.Load<TextAsset>(path).text;
        string[] lines = text.Split(new[] { '\r', '\n' });

        if (shuffle)
            Shuffle<string>(new System.Random(), lines);

        return lines;
    }

    //thanks Matt Howells
    private void Shuffle<T> (System.Random rng, T[] array)
    {
        int n = array.Length;
        while (n > 1) 
        {
            int k = rng.Next(n--);
            T temp = array[n];
            array[n] = array[k];
            array[k] = temp;
        }
    }

    //thanks Virtlink from stackoverflow
    protected static void WriteAllLinesNoExtraNewline(string path, params string[] lines)
    {
        if (path == null)
            throw new UnityException("path argument should not be null");
        if (lines == null)
            throw new UnityException("lines argument should not be null");

        using (var stream = System.IO.File.OpenWrite(path))
        {
            using (System.IO.StreamWriter writer = new System.IO.StreamWriter(stream))
            {
                if (lines.Length > 0)
                {
                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        writer.WriteLine(lines[i]);
                    }
                    writer.Write(lines[lines.Length - 1]);
                }
            }
        }
    }

    private void SetSessionNumber()
    {
        int nextSessionNumber = 0;
        UnityEPL.SetSessionNumber(0);
        while (System.IO.Directory.Exists(UnityEPL.GetDataPath()))
        {
            nextSessionNumber++;
            UnityEPL.SetSessionNumber(nextSessionNumber);
        }
    }

    private void Quit()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}