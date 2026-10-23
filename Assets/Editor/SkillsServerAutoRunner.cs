using UnityEditor;
using UnitySkills;

[InitializeOnLoad]
public static class SkillsServerAutoRunner
{
    static SkillsServerAutoRunner()
    {
        EditorPrefs.SetBool("UnitySkills_ServerShouldRun", true);
        try
        {
            if (!SkillsHttpServer.IsRunning)
            {
                SkillsHttpServer.Start(8090, true);
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogWarning("[SkillsServerAutoRunner] " + ex.Message);
        }
    }
}
