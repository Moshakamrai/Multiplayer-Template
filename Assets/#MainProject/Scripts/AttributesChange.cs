using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AttributesChange : MonoBehaviour
{

    public bool attckWindowStart;
    public bool attckWindowEnd;

    public PlayerCombat playerComabt;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void StartAttckWindow()
    {
        
        playerComabt.StartAttackWindow();
        
    }
    public void EndAttckWindow()    
    {

        playerComabt.EndAttackWindow();
    }
}
