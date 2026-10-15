using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
public class MenuScript : MonoBehaviour
{
    public Button button;
    public string scene;
    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OnClick()
    {
       
            SceneManager.LoadScene(scene);
 
    }

}
