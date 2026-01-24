using UnityEngine;

public class BoardInputHandler : MonoBehaviour
{
    [SerializeField] private BoardController _boardController;

    // We need board dimensions to calculate grid index, 
    // OR we can ask BoardController to convert World to Grid?
    // Better: BoardController should expose a way to get Grid from World, or InputHandler does the math if it knows params.
    // To keep it loosely coupled, InputHandler should just detect "Touch at World Pos" or handle the raycast and ask Board "What is here?".
    // But BoardController has the "HandleClick" which does the raycast logic currently.
    // Let's move the Raycast logic HERE.
    
    // We need the Board's origin and cell size to calc grid coords?
    // Or we just pass world pos to BoardController and let it decide?
    // "BoardController.ProcessInputAt(Vector3 worldPos)"?
    
    // Let's stick to the plan: InputHandler handles "Is User clicking and where?".
    // The previous logic handled "Perspective vs Ortho" raycast.
    
    private void Update()
    {
        // Don't process if paused (Time.timeScale == 0)
        if (Time.timeScale == 0f) return;

        // Don't process if game not active? BoardController checks that. 
        // We can just pass input.
        
        if (Input.GetMouseButtonDown(0))
        {
            HandleInput();
        }
    }

    private void HandleInput()
    {
        if (_boardController == null) return;
        if (Camera.main == null) return;

        // Robust input for both Perspective (3D) and Orthographic (2D) cameras
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane boardPlane = new Plane(Vector3.forward, Vector3.zero); // Plane at Z=0
        
        float distance;
        Vector3 mouseWorldPos;

        if (boardPlane.Raycast(ray, out distance))
        {
            mouseWorldPos = ray.GetPoint(distance);
        }
        else
        {
            // Fallback
             mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        }
        
        // Pass World Pos to Controller
        _boardController.OnInputAtPosition(mouseWorldPos);
    }
}
